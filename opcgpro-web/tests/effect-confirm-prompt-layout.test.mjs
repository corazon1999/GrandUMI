import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("是否发动效果使用中下部无全屏遮罩确认框", async () => {
  const source = await readSource("../src/components/game/PromptOverlay.tsx");

  assert.match(source, /const isEffectConfirm =/);
  assert.match(source, /options\[0\] === "是"/);
  assert.match(source, /options\[1\] === "否"/);
  assert.match(source, /data-effect-confirm-layer/);
  assert.match(source, /pointer-events-none fixed inset-0 z-50/);
  assert.match(source, /data-effect-confirm-dialog/);
  assert.match(source, /bottom-\[clamp\(1rem,7vh,4\.5rem\)\]/);
});

test("效果确认框提供旋转确认取消按钮与右上角收起箭头", async () => {
  const source = await readSource("../src/components/game/PromptOverlay.tsx");

  assert.match(source, /EffectDecisionButton/);
  assert.match(source, /repeat: Infinity/);
  assert.match(source, /label="取消"[\s\S]*?submitServerPrompt\(\["1"\]\)/);
  assert.match(source, /label="确认"[\s\S]*?submitServerPrompt\(\["0"\], true\)/);
  assert.match(source, /aria-label="收起效果确认框"/);
  assert.match(source, /aria-label="展开效果确认框"/);
  assert.match(source, /<PromptChevron expanded \/>/);
  assert.match(source, /<PromptChevron expanded=\{false\} \/>/);
});

test("领袖目标在通用选择面板中显示明确标识", async () => {
  const source = await readSource("../src/components/game/PromptOverlay.tsx");

  assert.match(source, /const isLeaderChoice =/);
  assert.match(source, /choiceZone === "leader"/);
  assert.match(source, /isLeaderChoice \? " · 领袖"/);
});

test("选择面板隐藏与恢复按钮适配手机竖屏安全区", async () => {
  const source = await readSource("../src/components/game/PromptOverlay.tsx");

  assert.match(source, /const promptToggleStyle =/);
  assert.match(source, /var\(--layout-safe-left, 0px\)/);
  assert.match(source, /var\(--layout-safe-bottom, 0px\)/);
  assert.equal(source.match(/style=\{promptToggleStyle\}/g)?.length, 2);
  assert.equal(
    source.match(/flex h-12 w-12 items-center justify-center rounded-full bg-slate-800\/90/g)?.length,
    2,
  );
});
