import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";
import { create } from "zustand";
import { immer } from "zustand/middleware/immer";
import * as cinematic from "../src/lib/gameCinematic.mjs";

const source = await readFile(new URL("../src/store/gameStore.ts", import.meta.url), "utf8");
function fixture() {
  const module = { exports: {} };
  const dependencies = { zustand: { create }, "zustand/middleware/immer": { immer }, "../lib/gameCinematic.mjs": cinematic };
  vm.runInNewContext(ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, {
    module, exports: module.exports, Date, console, structuredClone, setTimeout, clearTimeout,
    require(name) {
      assert.ok(dependencies[name], `缺少真实模块依赖：${name}`);
      return dependencies[name];
    },
  });
  return module.exports.useGameStore;
}

const prompt = { promptId: "cost-1", kind: "ReturnOwnDon", validChoices: ["don-1"], minChoose: 1, maxChoose: 1 };
function snapshot(tick, isGameOver, pendingPrompt = prompt) {
  return { roomId: "结束对局", tick, phase: "Main", currentTurn: true, isGameOver, winnerIsMe: true,
    my: { fieldCards: [], handCardNumbers: [] }, opponent: { fieldCards: [], handCardNumbers: [] }, pendingPrompt };
}

test("投降的终局快照清除旧成本提示，即使服务器仍附带旧提示", () => {
  const store = fixture();
  store.getState().syncFromServer(snapshot(1, false));
  assert.equal(store.getState().pendingPrompt.promptId, "cost-1");
  store.getState().syncFromServer(snapshot(2, true));
  assert.equal(store.getState().pendingPrompt, null);
  assert.equal(store.getState().isGameOver, true);
});

test("DuelOver在缺少最终快照时立即清理服务器提示及本地腾位选择", () => {
  const store = fixture();
  store.getState().syncFromServer(snapshot(1, false));
  store.getState().openLocalOverflow(3);
  store.getState().finishFromDuelOver(true, "对手投降");
  assert.equal(store.getState().pendingPrompt, null);
  assert.equal(store.getState().localOverflowHandIndex, null);
  assert.equal(store.getState().isGameOver, true);
});

test("迟到的进行中快照不能重新打开终局成本提示", () => {
  const store = fixture();
  store.getState().syncFromServer(snapshot(2, true));
  store.getState().syncFromServer(snapshot(1, false));
  assert.equal(store.getState().pendingPrompt, null);
  assert.equal(store.getState().isGameOver, true);
});
