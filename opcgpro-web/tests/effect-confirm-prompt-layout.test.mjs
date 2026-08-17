import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("是否发动效果使用旋转画布容器尺寸而非物理视口", async () => {
  const source = await readSource("../src/components/game/PromptOverlay.tsx");

  assert.match(source, /const isEffectConfirm =/);
  assert.match(source, /options\[0\] === "是"/);
  assert.match(source, /options\[1\] === "否"/);
  assert.match(source, /data-effect-confirm-layer/);
  assert.match(source, /pointer-events-none fixed inset-0 z-50/);
  assert.match(source, /data-effect-confirm-dialog/);
  assert.match(source, /const effectPromptStyle =/);
  assert.match(source, /clamp\(0\.75rem, 4cqh, 1\.5rem\)/);
  assert.match(source, /100cqw - 2rem/);
  assert.match(source, /100cqh - 2rem/);
  assert.match(source, /var\(--layout-safe-left, 0px\)/);
  assert.match(source, /var\(--layout-safe-right, 0px\)/);
});

test("效果确认框使用紧凑横向布局与至少 44px 的操作按钮", async () => {
  const source = await readSource("../src/components/game/PromptOverlay.tsx");

  assert.match(source, /flex flex-col @\[640px\]:flex-row/);
  assert.match(source, /onClick=\{\(\) => submitServerPrompt\(\["1"\]\)\}[\s\S]*?h-11 min-w-20[\s\S]*?aria-label="取消"/);
  assert.match(source, /onClick=\{\(\) => submitServerPrompt\(\["0"\], true\)\}[\s\S]*?h-11 min-w-20[\s\S]*?aria-label="确认"/);
  assert.match(source, /className="flex h-11 w-11[\s\S]*?aria-label="收起效果确认框"/);
  assert.doesNotMatch(source, /EffectDecisionButton/);
  assert.doesNotMatch(source, /repeat: Infinity/);
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
  assert.match(source, /left: "calc\(0\.75rem \+ var\(--layout-safe-left, 0px\)\)"/);
  assert.match(source, /bottom: "calc\(4\.5rem \+ var\(--layout-safe-bottom, 0px\)\)"/);
  assert.match(source, /var\(--layout-safe-left, 0px\)/);
  assert.match(source, /var\(--layout-safe-bottom, 0px\)/);
  assert.doesNotMatch(source, /promptToggleOffset/);
  assert.equal(source.match(/style=\{promptToggleStyle\}/g)?.length, 2);
  assert.equal(
    source.match(/flex h-12 w-12 items-center justify-center rounded-full bg-slate-800\/90/g)?.length,
    2,
  );
});
