import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

const source = await readFile(new URL("../src/net/TournamentClient.ts", import.meta.url), "utf8");
function fixture() {
  const handlers = new Map(); const timers = new Map(); const sent = []; let sequence = 0; let connected = true;
  const bus = {
    on(name, handler) { const list = handlers.get(name) ?? new Set(); list.add(handler); handlers.set(name, list); },
    off(name, handler) { handlers.get(name)?.delete(handler); },
    emit(name, value) { for (const handler of [...(handlers.get(name) ?? [])]) handler(value); },
  };
  const module = { exports: {} };
  const dependencies = {
    "zustand": { create: initial => { let state = initial(); return { setState: values => { state = { ...state, ...values }; }, getState: () => state }; } },
    "./eventBus": { eventBus: bus },
    "./NetManager": { NetManager: { send: message => { sent.push(message); return connected; } } },
  };
  vm.runInNewContext(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText, {
    module, exports: module.exports, require: name => dependencies[name],
    crypto: { randomUUID: () => String(++sequence) },
    setTimeout: callback => { const id = ++sequence; timers.set(id, callback); return id; },
    clearTimeout: id => timers.delete(id),
  });
  return { api: module.exports, bus, sent, timers, handlers, disconnect: () => { connected = false; } };
}

test("赛事并发只读请求按编号接收响应且不串场", async () => {
  const f = fixture();
  const first = f.api.requestTournament("detail", { id: "甲赛" });
  const second = f.api.requestTournament("detail", { id: "乙赛" });
  f.bus.emit("message", { proto: "MsgTournament", requestId: f.sent[1].requestId, result: true, marker: "乙" });
  f.bus.emit("message", { proto: "MsgTournament", requestId: f.sent[0].requestId, result: true, marker: "甲" });
  assert.equal((await first).marker, "甲"); assert.equal((await second).marker, "乙");
  assert.equal(f.handlers.get("message").size, 0); assert.equal(f.timers.size, 0);
});

test("赛事写请求保留版本和确认令牌，拒绝与超时均释放监听", async () => {
  const f = fixture();
  const pending = f.api.requestTournament("endRound", { id: "赛事", revision: 7, confirmationToken: "二次确认" });
  assert.equal(f.sent[0].revision, 7); assert.equal(f.sent[0].confirmationToken, "二次确认");
  f.bus.emit("message", { proto: "MsgTournament", requestId: f.sent[0].requestId, result: false, logStr: "状态已更新" });
  await assert.rejects(pending, /状态已更新/);
  const timeout = f.api.requestTournament("list");
  [...f.timers.values()][0]();
  await assert.rejects(timeout, /请求超时/);
  assert.equal(f.handlers.get("message").size, 0); assert.equal(f.timers.size, 0);
});

test("未连接不遗留请求，重复注册不重复监听且账号替换清除赛事时钟", async () => {
  const f = fixture(); f.disconnect();
  await assert.rejects(f.api.requestTournament("seat"), /网络未连接/);
  assert.equal(f.timers.size, 0);
  f.api.registerTournamentProtocols(); f.api.registerTournamentProtocols();
  assert.equal(f.handlers.get("message").size, 1);
  f.bus.emit("message", { proto: "MsgTournamentClock", tournamentId: "赛事甲", serverNow: Date.now() });
  assert.equal(f.api.useTournamentClock.getState().clock.tournamentId, "赛事甲");
  f.bus.emit("sessionReplaced", { reason: "账号已替换" });
  assert.equal(f.api.useTournamentClock.getState().clock, null);
});

test("签到只提交明确的二次确认标记，牌桌QQ随赛事时钟同步并在换号时清理", async () => {
  const f = fixture();
  const pending = f.api.requestTournament("checkin", { id: "赛事", revision: 8, confirmCheckin: true });
  assert.equal(f.sent[0].confirmCheckin, true);
  f.bus.emit("message", { proto: "MsgTournament", requestId: f.sent[0].requestId, result: true });
  await pending;
  f.api.registerTournamentProtocols();
  f.bus.emit("message", { proto: "MsgTournamentClock", roomId: "room-1", contacts: [{ account: "p0", name: "甲", qq: "123456789" }], serverNow: Date.now() });
  assert.equal(f.api.useTournamentClock.getState().clock.contacts[0].qq, "123456789");
  f.bus.emit("sessionReplaced", {});
  assert.equal(f.api.useTournamentClock.getState().clock, null);
});
