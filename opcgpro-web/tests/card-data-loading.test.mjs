import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { setImmediate as nextTurn } from "node:timers/promises";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

const dataSource = await readFile(new URL("../src/data/fetchCardData.ts", import.meta.url), "utf8");
const loaderSource = await readFile(new URL("../src/data/CardLoader.ts", import.meta.url), "utf8");
function compile(source) {
  return ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText;
}
function fixture(fetch, hostname = "ygo.grand-umi.com") {
  const timers = new Map();
  let nextId = 0;
  const require = name => {
    if (name === "./dataVersion") return { DATA_VERSION: "诊断版本" };
    if (name === "./cardSets") return { CARD_SET_PATHS: { OP16: "/data/OP16.json", P: "/data/P.json" } };
    if (name === "./fetchCardData") return dataModule.exports;
    throw Error(`未配置回归依赖：${name}`);
  };
  const globals = { require, fetch, AbortController, URL, Error, window: { location: { hostname } }, console: { warn() {} },
    setTimeout(callback) { const id = ++nextId; timers.set(id, callback); return id; },
    clearTimeout(id) { timers.delete(id); } };
  const dataModule = { exports: {} };
  vm.runInNewContext(compile(dataSource), { ...globals, module: dataModule, exports: dataModule.exports });
  const loaderModule = { exports: {} };
  vm.runInNewContext(compile(loaderSource), { ...globals, module: loaderModule, exports: loaderModule.exports });
  return { data: dataModule.exports, loader: loaderModule.exports, async expire() {
    const first = timers.entries().next().value;
    assert.ok(first, "应存在尚未结束的资料请求计时器");
    timers.delete(first[0]); first[1](); await nextTurn();
  }, timers };
}
const response = data => ({ ok: true, json: async () => data });

test("资料请求持续等待时有限超时并切换直连，附带内容版本", async () => {
  const calls = [];
  let aborted = false;
  const f = fixture((url, options) => {
    calls.push(url);
    if (calls.length === 1) {
      options.signal.addEventListener("abort", () => { aborted = true; });
      return new Promise(() => {});
    }
    return Promise.resolve(response({ restored: true }));
  });
  const pending = f.data.fetchCardData("/data/OP16.json");
  await f.expire();
  assert.deepEqual(await pending, { restored: true });
  assert.equal(aborted, true);
  assert.match(calls[1], /^https:\/\/direct\.grand-umi\.com\/data\/OP16\.json\?v=/);
  assert.equal(f.timers.size, 0);
});

test("响应头成功但 JSON 响应体停滞也会超时并恢复", async () => {
  let calls = 0;
  const f = fixture(async () => ++calls === 1 ? { ok: true, json: () => new Promise(() => {}) } : response(["恢复"]));
  const pending = f.data.fetchCardData("/data/imageManifest.json");
  await nextTurn();
  await f.expire();
  assert.deepEqual(await pending, ["恢复"]);
  assert.equal(calls, 2);
});

test("全部线路失败后结束有限重试，测试环境不会访问正式线路", async () => {
  const calls = [];
  const f = fixture(async url => { calls.push(url); throw Error("模拟断网"); }, "localhost");
  await assert.rejects(f.data.fetchCardData("/data/OP16.json"), /模拟断网/);
  assert.equal(calls.length, 2);
  assert.ok(calls.every(url => url.startsWith("/data/")));
});

test("同一卡集并发请求复用一次下载且清单失败保留已成功资料", async () => {
  const calls = [];
  const raw = [{ number: "OP16-001", name: "回归卡牌", type: "领航", power: "5000" }];
  const f = fixture(async url => {
    calls.push(url);
    const path = new URL(url, "https://ygo.grand-umi.com").pathname;
    if (path.includes("_playability")) throw Error("模拟可用状态请求失败");
    return response(path.includes("imageManifest") ? { "OP16-001": ["/cards/op16/异画.png"] } : raw);
  });
  const one = f.loader.loadCardSet("OP16");
  const two = f.loader.loadCardSet("OP16");
  assert.equal(one, two);
  await one;
  assert.equal(calls.filter(url => url.includes("OP16.json")).length, 1);
  assert.equal(f.loader.getCard("OP16-001").sprite, "/cards/op16/异画.png");
});

test("卡集本体不等待慢清单才开始下载，失败后可重新加载", async () => {
  const calls = [];
  let broken = true;
  const f = fixture(async url => {
    calls.push(url);
    const path = new URL(url, "https://ygo.grand-umi.com").pathname;
    if (path.endsWith("OP16.json") && broken) throw Error("模拟卡集失败");
    return response(path.includes("OP16.json") ? [{ number: "OP16-001", type: "领航" }] : {});
  });
  await assert.rejects(f.loader.loadCardSet("OP16"), /模拟卡集失败/);
  assert.ok(calls[0].includes("OP16.json"));
  broken = false;
  await f.loader.loadCardSet("OP16");
  assert.ok(f.loader.getCard("OP16-001"));
});

test("预加载卡组包含宣传卡 P 卡集", async () => {
  const calls = [];
  const f = fixture(async url => {
    calls.push(url);
    const path = new URL(url, "https://ygo.grand-umi.com").pathname;
    return response(path.endsWith(".json") && !path.includes("Manifest") && !path.includes("playability") ? [] : {});
  });
  await f.loader.preloadDeckCards("OP16-001\nP-136\nP-136");
  assert.equal(calls.filter(url => url.includes("P.json")).length, 1);
});
