import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

const homeSource = await readFile(new URL("../src/net/HomeProtocol.ts", import.meta.url), "utf8");

function protocolFixture() {
  const handlers = new Map();
  const values = new Map();
  const blockedKeys = new Set();
  const net = {
    playerName: "玩家甲", opponentName: "", matchState: "idle", navigateTo: null,
    setMatchQueueKind(value) { this.matchQueueKind = value; },
    setMatchState(value) { this.matchState = value; },
    setOpponentName(value) { this.opponentName = value; },
    setNavigateTo(value) { this.navigateTo = value; },
  };
  const game = { resets: 0, resetGame() { this.resets++; }, setMode(value) { this.mode = value; } };
  const dependencies = {
    "./TournamentClient": { registerTournamentProtocols() {}, clearTournamentClock() {} },
    "./NetManager": { NetManager: { resetGameStateBaseline() {} } },
    "./eventBus": { eventBus: { on: (name, handler) => handlers.set(name, handler) } },
    "@/store/netStore": { useNetStore: { getState: () => net } },
    "@/store/gameStore": { useGameStore: { getState: () => game } },
    "@/components/ui/MessageBox": { showMessage() {} },
  };
  const module = { exports: {} };
  vm.runInNewContext(ts.transpileModule(homeSource, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, {
    module, exports: module.exports, window: {}, console: { warn() {} },
    sessionStorage: {
      setItem(key, value) {
        if (blockedKeys.has(key)) throw new Error("浏览器会话存储已满");
        values.set(key, value);
      },
    },
    require: name => dependencies[name] ?? {},
  });
  module.exports.registerHomeProtocols();
  return { net, game, values, blockedKeys, receive: msg => handlers.get("message")(msg) };
}

test("匹配成功后会话存储写入失败仍能收到开局并发起入场", () => {
  const f = protocolFixture();
  f.receive({ proto: "MsgMatchFound", queueKind: "ranked", opponentName: "玩家乙" });
  assert.equal(f.net.matchState, "matched");
  f.blockedKeys.add("myDeck");
  assert.doesNotThrow(() => f.receive({ proto: "MsgGameStart", MainDeck: "OP15-001" }));
  assert.equal(f.net.navigateTo, "/game");
  assert.equal(f.game.mode, "Player");
  assert.equal(f.game.resets, 1);
});

test("匹配类型的可选缓存失败不阻断匹配成功状态", () => {
  const f = protocolFixture();
  f.blockedKeys.add("grandumi_match_queue_kind");
  assert.doesNotThrow(() => f.receive({ proto: "MsgMatchFound", queueKind: "ranked", opponentName: "玩家乙" }));
  assert.equal(f.net.matchState, "matched");
  assert.equal(f.net.opponentName, "玩家乙");
});

test("正常开局仍缓存卡组与昵称", () => {
  const f = protocolFixture();
  f.receive({ proto: "MsgMatchFound", queueKind: "casual", opponentName: "玩家乙" });
  f.receive({ proto: "MsgGameStart", MainDeck: "OP15-001" });
  assert.equal(f.values.get("myDeck"), "OP15-001");
  assert.equal(f.values.get("myName"), "玩家甲");
  assert.equal(f.values.get("opponentName"), "玩家乙");
  assert.equal(f.net.navigateTo, "/game");
});

const recoverySource = await readFile(new URL("../src/net/gameEntryRecovery.ts", import.meta.url), "utf8");

function recoveryFixture() {
  const timers = new Map();
  const events = [];
  const state = { pending: true, connected: true };
  const module = { exports: {} };
  vm.runInNewContext(ts.transpileModule(recoverySource, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, {
    module, exports: module.exports,
    setTimeout: (callback, delay) => { timers.set(delay, callback); return delay; },
    clearTimeout: timer => timers.delete(timer),
  });
  const stop = module.exports.startGameEntryRecovery({
    isPending: () => state.pending,
    isConnected: () => state.connected,
    requestState: () => events.push("补取状态"),
    retryNavigation: () => events.push("重试导航"),
    reloadGame: () => events.push("整页恢复"),
  });
  return { timers, events, state, stop };
}

test("缺少开局消息时先补取状态，导航持续停滞才整页恢复", () => {
  const f = recoveryFixture();
  f.timers.get(4_000)();
  assert.deepEqual(f.events, ["补取状态", "重试导航"]);
  f.timers.get(12_000)();
  assert.deepEqual(f.events, ["补取状态", "重试导航", "整页恢复"]);
});

test("已到达牌桌或对局已结束时不再重试与刷新", () => {
  const f = recoveryFixture();
  f.state.pending = false;
  for (const callback of f.timers.values()) callback();
  assert.deepEqual(f.events, []);
});

test("断线期间交给既有重连流程，不发送无效状态请求或刷新页面", () => {
  const f = recoveryFixture();
  f.state.connected = false;
  for (const callback of f.timers.values()) callback();
  assert.deepEqual(f.events, []);
});

test("离开入场阶段或卸载时取消所有恢复任务", () => {
  const f = recoveryFixture();
  f.stop();
  assert.equal(f.timers.size, 0);
  assert.deepEqual(f.events, []);
});

const providerSource = await readFile(new URL("../src/components/NetProvider.tsx", import.meta.url), "utf8");

function providerFixture(pathname) {
  const effects = [];
  const navigation = [];
  let recoveries = 0;
  const net = { loggedIn: true, matchState: "matched", navigateTo: "/game",
    setNavigateTo(value) { this.navigateTo = value; } };
  const useNetStore = selector => selector(net);
  useNetStore.getState = () => net;
  const dependencies = {
    react: { useEffect: effect => effects.push(effect) },
    "react/jsx-runtime": { jsx() {}, jsxs() {} },
    "next/navigation": { usePathname: () => pathname, useRouter: () => ({
      prefetch() {}, push: path => navigation.push(path), replace() {},
    }) },
    "@/hooks/useNet": { useNet() {} },
    "@/store/netStore": { useNetStore },
    "@/net/gameEntryRecovery": { startGameEntryRecovery() { recoveries++; return () => {}; } },
  };
  const module = { exports: {} };
  vm.runInNewContext(ts.transpileModule(providerSource, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX },
  }).outputText, { module, exports: module.exports, require: name => dependencies[name] ?? {} });
  module.exports.default({ children: null });
  for (const effect of effects) effect();
  return { net, navigation, recoveries };
}

test("发起异步路由后保留入场请求，并启动恢复而非立即消费", () => {
  const f = providerFixture("/home");
  assert.deepEqual(f.navigation, ["/game"]);
  assert.equal(f.net.navigateTo, "/game");
  assert.equal(f.recoveries, 1);
});

test("确认到达对局页后消费导航请求，不再启动入场恢复", () => {
  const f = providerFixture("/game");
  assert.equal(f.net.navigateTo, null);
  assert.equal(f.recoveries, 0);
  assert.deepEqual(f.navigation, []);
});
