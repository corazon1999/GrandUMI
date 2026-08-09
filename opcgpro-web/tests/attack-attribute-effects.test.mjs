import assert from "node:assert/strict";
import { readFile, stat } from "node:fs/promises";
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

test("未知属性使用青粉色差主题", () => {
  assert.equal(ATTACK_ATTRIBUTE_THEMES["?"].secondary, "#22d3ee");
  assert.equal(ATTACK_ATTRIBUTE_THEMES["?"].accent, "#f472b6");
});

test("电影级视觉层包含六类独立命中结构和分层动画", async () => {
  const component = await readFile(
    new URL("../src/components/game/AttributeAttackEffect.tsx", import.meta.url),
    "utf8",
  );

  for (const signature of [
    "SlashImpact",
    "StrikeImpact",
    "ShotImpact",
    "SpecialImpact",
    "KnowledgeImpact",
    "UnknownImpact",
  ]) {
    assert.match(component, new RegExp(`function ${signature}\\b`));
  }

  assert.match(component, /data-attack-vfx="cinematic"/);
  assert.match(component, /feGaussianBlur/);
  assert.match(component, /animateMotion/);
  assert.match(component, /AttributeTrail/);
  assert.match(component, /CinematicTexture/);
  assert.match(component, /PARTICLE_ANGLES/);
});

test("六种属性的电影级材质图均已生成并接入", async () => {
  const names = ["slash", "strike", "shot", "special", "knowledge", "unknown"];
  const files = await Promise.all(names.map((name) => (
    stat(new URL(`../public/vfx/attack-${name}.webp`, import.meta.url))
  )));

  assert.ok(files.every((file) => file.size > 40_000));
});
