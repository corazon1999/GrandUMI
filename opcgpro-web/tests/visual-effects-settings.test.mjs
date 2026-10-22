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

test("新用户、旧设置、损坏数据和非布尔开关均默认开启", () => {
  for (const raw of [null, "损坏", "null", '{}', '{"animationSpeed":"standard"}', '{"visualEffectsEnabled":"true"}']) {
    assert.equal(loadSettings(raw).store.getState().visualEffectsEnabled, true);
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
  assert.equal(state.saved().visualEffectsDefaultsVersion, 1);
  assert.equal(loadSettings(JSON.stringify(state.saved())).store.getState().visualEffectsEnabled, false);
});

test("已有浏览器保存的关闭开关和关闭速度也恢复开启，其他偏好保持原值", () => {
  const state = loadSettings(JSON.stringify({
    visualEffectsEnabled: false, animationSpeed: "off", alwaysPromptOnLifeReveal: true,
    cardSize: "sm", confirmAttachDon: true, hideOpponentCardBack: true,
  })).store.getState();
  assert.equal(state.visualEffectsEnabled, true);
  assert.equal(state.animationSpeed, "standard");
  assert.equal(state.alwaysPromptOnLifeReveal, true);
  assert.equal(state.cardSize, "sm");
  assert.equal(state.confirmAttachDon, true);
  assert.equal(state.hideOpponentCardBack, true);
});

test("迁移只执行一次，更新后主动关闭并刷新仍保持关闭", () => {
  const first = loadSettings('{"visualEffectsEnabled":false,"animationSpeed":"fast"}');
  assert.equal(first.store.getState().visualEffectsEnabled, true);
  first.store.getState().setVisualEffectsEnabled(false);
  const second = loadSettings(JSON.stringify(first.saved()));
  assert.equal(second.store.getState().visualEffectsEnabled, false);
  assert.equal(second.store.getState().animationSpeed, "fast");
  second.store.getState().setCardSize("lg");
  const third = loadSettings(JSON.stringify(second.saved())).store.getState();
  assert.equal(third.visualEffectsEnabled, false);
  assert.equal(third.cardSize, "lg");
});

test("迁移标记与卡背偏好共存，卡背开关的两个方向都可持久化", () => {
  const state = loadSettings('{"visualEffectsEnabled":false}');
  assert.equal(state.store.getState().hideOpponentCardBack, false);
  state.store.getState().setHideOpponentCardBack(true);
  assert.equal(state.saved().hideOpponentCardBack, true);
  assert.equal(state.saved().visualEffectsDefaultsVersion, 1);
  const reloaded = loadSettings(JSON.stringify(state.saved()));
  assert.equal(reloaded.store.getState().hideOpponentCardBack, true);
  reloaded.store.getState().setHideOpponentCardBack(false);
  assert.equal(loadSettings(JSON.stringify(reloaded.saved())).store.getState().hideOpponentCardBack, false);
});

test("旧用户关闭过对局动画时，主动开启总开关会恢复可用的标准速度", () => {
  const state = loadSettings('{"animationSpeed":"off"}');
  state.store.getState().setVisualEffectsEnabled(true);
  assert.equal(state.store.getState().animationSpeed, "standard");
  assert.equal(state.saved().visualEffectsEnabled, true);
});

function reducedEffects(settings, systemReducedMotion) {
  const exports = {};
  const context = vm.createContext({
    exports,
    window: { matchMedia: () => ({ matches: systemReducedMotion }) },
    require: name => name === "react"
      ? { useSyncExternalStore: (_subscribe, snapshot) => snapshot() }
      : { useSettingsStore: selector => selector(settings) },
  });
  const hook = readFileSync(new URL("../src/hooks/useVisualEffects.ts", import.meta.url), "utf8");
  vm.runInContext(ts.transpileModule(hook, { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText, context);
  return exports.useReducedVisualEffects();
}

test("默认尊重系统减少动态效果；主动开启后对战与观战均遵循浏览器选择", () => {
  const state = loadSettings(null);
  assert.equal(reducedEffects(state.store.getState(), true), true);
  assert.equal(reducedEffects(state.store.getState(), false), false);
  state.store.getState().setVisualEffectsEnabled(true);
  assert.equal(reducedEffects(state.store.getState(), true), false);
  assert.equal(state.saved().visualEffectsOverrideReducedMotion, true);
  const reloaded = loadSettings(JSON.stringify(state.saved()));
  assert.equal(reducedEffects(reloaded.store.getState(), true), false);
  reloaded.store.getState().setCardSize("sm");
  assert.equal(reducedEffects(loadSettings(JSON.stringify(reloaded.saved())).store.getState(), true), false);
  reloaded.store.getState().setVisualEffectsEnabled(false);
  assert.equal(reducedEffects(reloaded.store.getState(), true), true);
  assert.equal(reducedEffects(reloaded.store.getState(), false), true);
  assert.equal(reloaded.saved().visualEffectsOverrideReducedMotion, false);
});

test("旧设置和非布尔覆盖值不会绕过系统减少动态效果", () => {
  for (const raw of [null, '{}', '{"visualEffectsOverrideReducedMotion":"true"}']) {
    assert.equal(reducedEffects(loadSettings(raw).store.getState(), true), true);
  }
});
