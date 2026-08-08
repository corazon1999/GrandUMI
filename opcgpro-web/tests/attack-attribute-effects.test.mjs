import assert from "node:assert/strict";
import test from "node:test";
import {
  ATTACK_ATTRIBUTES,
  ATTACK_ATTRIBUTE_THEMES,
  composeAttackTheme,
  normalizeAttackAttributes,
} from "../src/lib/attackAttributeEffects.ts";

test("六种攻击属性均有独立主题", () => {
  assert.deepEqual(ATTACK_ATTRIBUTES, ["斩", "打", "射", "特", "知", "?"]);

  const primaryColors = ATTACK_ATTRIBUTES.map((attribute) => ATTACK_ATTRIBUTE_THEMES[attribute].primary);
  assert.equal(new Set(primaryColors).size, ATTACK_ATTRIBUTES.length);
});

test("兼容知属性历史别名和未知属性写法", () => {
  assert.deepEqual(normalizeAttackAttributes("智"), ["知"]);
  assert.deepEqual(normalizeAttackAttributes("？"), ["?"]);
  assert.deepEqual(normalizeAttackAttributes("-"), ["?"]);
  assert.deepEqual(normalizeAttackAttributes(""), ["?"]);
  assert.deepEqual(normalizeAttackAttributes("黑"), ["?"]);
});

test("多属性按卡面顺序保留全部子属性并去重", () => {
  assert.deepEqual(normalizeAttackAttributes("斩/打"), ["斩", "打"]);
  assert.deepEqual(normalizeAttackAttributes("特／智/特"), ["特", "知"]);
  assert.deepEqual(normalizeAttackAttributes(" 射 / ？ "), ["射", "?"]);
});

test("复合主题包含每个子属性的颜色和标签", () => {
  const theme = composeAttackTheme(["斩", "打"]);

  assert.equal(theme.label, "斩/打");
  assert.equal(theme.isComposite, true);
  assert.deepEqual(theme.attributes, ["斩", "打"]);
  assert.deepEqual(theme.colors, [
    ATTACK_ATTRIBUTE_THEMES.斩.primary,
    ATTACK_ATTRIBUTE_THEMES.斩.secondary,
    ATTACK_ATTRIBUTE_THEMES.打.primary,
    ATTACK_ATTRIBUTE_THEMES.打.secondary,
  ]);
});

test("单属性主题保留主色、辅色和高光色", () => {
  const theme = composeAttackTheme(["射"]);

  assert.equal(theme.isComposite, false);
  assert.deepEqual(theme.colors, [
    ATTACK_ATTRIBUTE_THEMES.射.primary,
    ATTACK_ATTRIBUTE_THEMES.射.secondary,
    ATTACK_ATTRIBUTE_THEMES.射.accent,
  ]);
});
