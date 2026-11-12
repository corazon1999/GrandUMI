import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

const source = await readFile(new URL("../src/net/HomeProtocol.ts", import.meta.url), "utf8");

function fixture() {
  const handlers = new Map();
  const messages = [];
  const net = {
    matchState: "matching",
    navigateTo: null,
    setMatchState(value) { this.matchState = value; },
    setNavigateTo(value) { this.navigateTo = value; },
  };
  const dependencies = {
    "./TournamentClient": { registerTournamentProtocols() {}, clearTournamentClock() {} },
    "./NetManager": { NetManager: {} },
    "./eventBus": { eventBus: { on: (name, handler) => handlers.set(name, handler) } },
    "@/store/netStore": { useNetStore: { getState: () => net } },
    "@/store/gameStore": { useGameStore: { getState: () => ({}) } },
    "@/components/ui/MessageBox": { showMessage: (text, kind) => messages.push({ text, kind }) },
  };
  const module = { exports: {} };
  vm.runInNewContext(ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, {
    module,
    exports: module.exports,
    console: { warn() {} },
    require: name => dependencies[name] ?? {},
  });
  module.exports.registerHomeProtocols();
  return { net, messages, receive: msg => handlers.get("message")(msg) };
}

for (const proto of ["MsgEnterMatch", "MsgEnterBotMatch"]) {
  test(`${proto} 确认服务端正在恢复时立即返回牌桌且不再显示错误`, () => {
    const f = fixture();
    f.receive({
      proto,
      result: false,
      errorCode: "account_in_game",
      roomState: "active_game",
      recoveryRequested: true,
      logStr: "检测到尚未结束的对局，正在返回牌桌。",
    });
    assert.equal(f.net.matchState, "idle");
    assert.equal(f.net.navigateTo, "/game");
    assert.deepEqual(f.messages, [{ text: "检测到尚未结束的对局，正在返回牌桌。", kind: "info" }]);
  });
}

test("未能恢复的活动对局仍显示明确错误且不盲目跳转", () => {
  const f = fixture();
  f.receive({
    proto: "MsgEnterMatch",
    result: false,
    errorCode: "account_in_game",
    roomState: "active_game",
    recoveryRequested: false,
    logStr: "恢复失败",
  });
  assert.equal(f.net.matchState, "idle");
  assert.equal(f.net.navigateTo, null);
  assert.deepEqual(f.messages, [{ text: "恢复失败", kind: "error" }]);
});

test("终局收尾提示不会被误判为活动对局恢复", () => {
  const f = fixture();
  f.receive({
    proto: "MsgEnterMatch",
    result: false,
    errorCode: "terminal_finalizing",
    roomState: "terminal_finalizing",
    recoveryRequested: false,
    logStr: "上一局已结束，服务器正在完成终局收尾，请稍候重试。",
  });
  assert.equal(f.net.navigateTo, null);
  assert.equal(f.messages[0].kind, "error");
});
