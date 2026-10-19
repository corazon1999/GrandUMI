import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

const require = createRequire(import.meta.url);
const source = readFileSync(new URL("../src/store/settingsStore.ts", import.meta.url), "utf8");

function loadSettings(saved) {
  let raw = saved;
  const exports = {};
  const context = vm.createContext({
    exports, window: {},
    localStorage: { getItem: () => raw, setItem: (_key, value) => { raw = value; } },
    require: name => name === "@/net/NetManager" ? { NetManager: { send() {} } } : require(name),
  });
  vm.runInContext(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText, context);
  return { store: exports.useSettingsStore, saved: () => JSON.parse(raw) };
}

test("新用户、旧设置、损坏数据和非布尔开关均默认静态", () => {
  for (const raw of [null, "损坏", "null", '{}', '{"animationSpeed":"standard"}', '{"visualEffectsEnabled":"true"}']) {
    assert.equal(loadSettings(raw).store.getState().visualEffectsEnabled, false);
  }
});

test("手动开启持久化；关闭后保留速度和其他设置，刷新不会复原", () => {
  const state = loadSettings('{"animationSpeed":"fast","hideOpponentCardBack":true}');
  state.store.getState().setVisualEffectsEnabled(true);
  assert.equal(state.saved().visualEffectsEnabled, true);
  assert.equal(loadSettings(JSON.stringify(state.saved())).store.getState().visualEffectsEnabled, true);
  state.store.getState().setVisualEffectsEnabled(false);
  assert.equal(state.saved().visualEffectsEnabled, false);
  assert.equal(state.saved().animationSpeed, "fast");
  assert.equal(state.saved().hideOpponentCardBack, true);
});

test("旧用户关闭过对局动画时，主动开启总开关会恢复可用的标准速度", () => {
  const state = loadSettings('{"animationSpeed":"off"}');
  state.store.getState().setVisualEffectsEnabled(true);
  assert.equal(state.store.getState().animationSpeed, "standard");
  assert.equal(state.saved().visualEffectsEnabled, true);
});
