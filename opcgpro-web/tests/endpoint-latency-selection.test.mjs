import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

const direct = "wss://direct.grand-umi.com/ws";
const proxy = "wss://ygo.grand-umi.com/ws";
const endpoints = [direct, proxy];
const source = await readFile(new URL("../src/net/endpointPreference.ts", import.meta.url), "utf8");
const compiled = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText;
const { EndpointPreferences } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString("base64")}`);

function fixture() {
  const values = new Map();
  const storage = { getItem: key => values.get(key) ?? null, setItem: (key, value) => values.set(key, value) };
  let now = 1_000_000;
  const clock = () => now;
  return { storage, values, clock, advance: ms => { now += ms; }, preferences: new EndpointPreferences(storage, clock) };
}

test("旧版永久记忆与过期成功记录不会让慢线路一直优先", () => {
  const f = fixture();
  f.values.set("grandumi_last_good_ws", proxy);
  assert.deepEqual(new EndpointPreferences(f.storage, f.clock).rank(endpoints), endpoints);
  f.preferences.recordSuccess(proxy);
  assert.equal(f.preferences.rank(endpoints)[0], proxy);
  f.advance(2 * 60 * 1000);
  assert.deepEqual(new EndpointPreferences(f.storage, f.clock).rank(endpoints), endpoints);
});

test("最近 RTT 比最近连接成功更重要，换页后仍选实测更快的线路", () => {
  const f = fixture();
  f.preferences.recordRtt(direct, 170);
  f.preferences.recordRtt(proxy, 398);
  f.preferences.recordSuccess(proxy);
  assert.equal(new EndpointPreferences(f.storage, f.clock).rank(endpoints)[0], direct);
  f.preferences.recordRtt(direct, 700);
  f.preferences.recordRtt(proxy, 200);
  assert.equal(f.preferences.rank(endpoints)[0], proxy);
});

test("只有备用线路样本或 RTT 过期时重新尝试配置首选", () => {
  const f = fixture();
  f.preferences.recordSuccess(proxy);
  f.preferences.recordRtt(proxy, 398);
  assert.equal(f.preferences.rank(endpoints)[0], direct);
  f.preferences.recordRtt(direct, 700);
  assert.equal(f.preferences.rank(endpoints)[0], proxy);
  f.advance(10 * 60 * 1000);
  assert.equal(f.preferences.rank(endpoints)[0], direct);
});

test("无效 RTT、未来时间与损坏存储不会影响连接", () => {
  const f = fixture();
  for (const value of [NaN, Infinity, -1, 60_000]) f.preferences.recordRtt(proxy, value);
  assert.deepEqual(f.preferences.rank(endpoints), endpoints);
  f.values.set("grandumi_ws_endpoint_quality_v1", "{损坏");
  assert.deepEqual(new EndpointPreferences(f.storage, f.clock).rank(endpoints), endpoints);
  const future = [{ url: proxy, succeededAt: f.clock() + 10_000, rttMs: null, measuredAt: 0 }];
  f.values.set("grandumi_ws_endpoint_quality_v1", JSON.stringify(future));
  assert.deepEqual(new EndpointPreferences(f.storage, f.clock).rank(endpoints), endpoints);
  const unavailable = { getItem() { throw Error("不可读"); }, setItem() { throw Error("不可写"); } };
  const preferences = new EndpointPreferences(unavailable, f.clock);
  preferences.recordSuccess(proxy);
  assert.equal(preferences.rank(endpoints)[0], proxy);
});

test("连接层隔离各线路前台 RTT，保持健康对局并优先避让熔断", async () => {
  const f = fixture();
  f.preferences.recordRtt(direct, 170);
  f.preferences.recordRtt(proxy, 398);
  const sockets = [];
  class FakeSocket {
    static OPEN = 1;
    readyState = 0;
    sent = [];
    constructor(url) { this.url = url; sockets.push(this); }
    send(value) { this.sent.push(JSON.parse(value)); }
    close() { this.readyState = 3; }
  }
  const managerSource = await readFile(new URL("../src/net/NetManager.ts", import.meta.url), "utf8");
  const output = ts.transpileModule(managerSource, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  const module = { exports: {} };
  const context = {
    module, exports: module.exports, WebSocket: FakeSocket, localStorage: f.storage,
    document: { visibilityState: "visible" },
    Date: { now: f.clock }, performance: { now: f.clock }, console: { info() {}, warn() {}, log() {} },
    setTimeout: () => 1, clearTimeout() {}, setInterval: () => 1, clearInterval() {},
    require(name) {
      if (name === "./eventBus") return { eventBus: { emit() {} } };
      if (name === "./endpointPreference") return { EndpointPreferences: class extends EndpointPreferences {
        constructor() { super(f.storage, f.clock); }
      } };
      if (name === "./sessionReplacement") return { SESSION_REPLACED_CLOSE_CODE: 4009, DEFAULT_SESSION_REPLACED_NOTICE: "会话替代" };
      throw Error(`未提供测试依赖：${name}`);
    },
  };
  vm.runInNewContext(output, context);
  const manager = module.exports.NetManager;
  manager.connect(endpoints);
  assert.equal(sockets[0].url, direct);
  sockets[0].readyState = FakeSocket.OPEN;
  sockets[0].onopen();
  sockets[0].onmessage({ data: JSON.stringify({ proto: "MsgSecret" }) });
  manager.retryNow(endpoints);
  assert.equal(sockets.length, 1);
  assert.equal(manager.isConnected, true);
  const pong = (socket, id, rtt) => {
    manager.pendingPings.set(id, f.clock());
    f.advance(rtt);
    socket.onmessage({ data: JSON.stringify({ proto: "MsgPing", id }) });
  };
  pong(sockets[0], "direct-1", 700);
  pong(sockets[0], "direct-2", 700);
  manager.disconnect();
  manager.markEndpointFailure(direct);
  manager.markEndpointFailure(direct);
  manager.connect(endpoints);
  assert.equal(sockets[1].url, proxy);
  sockets[1].readyState = FakeSocket.OPEN;
  sockets[1].onopen();
  sockets[1].onmessage({ data: JSON.stringify({ proto: "MsgSecret" }) });
  context.document.visibilityState = "hidden";
  pong(sockets[1], "proxy-background", 5_000);
  context.document.visibilityState = "visible";
  pong(sockets[1], "proxy-1", 200);
  const observations = JSON.parse(f.values.get("grandumi_ws_endpoint_quality_v1"));
  assert.equal(observations.find(entry => entry.url === proxy).rttMs, 200);
  assert.equal(observations.find(entry => entry.url === direct).rttMs, 700);
  assert.equal(new EndpointPreferences(f.storage, f.clock).rank(endpoints)[0], proxy);
  manager.disconnect();
});
