import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readPromptOverlay = () =>
  readFile(new URL("../src/components/game/PromptOverlay.tsx", import.meta.url), "utf8");

test("同名目标按唯一实例 ID 维护选中态并提交", async () => {
  const source = await readPromptOverlay();

  assert.match(source, /const isSelectedChoice = selectable && selected\.includes\(id\)/);
  assert.match(source, /data-prompt-choice-id=\{id\}/);
  assert.match(source, /GameRequest\.respondPrompt\(prompt\.promptId, chosen\)/);
  assert.match(source, /aria-pressed=\{selectable \? isSelectedChoice : undefined\}/);
});

test("普通目标选中后显示明确图标且保留阵营与场上位置", async () => {
  const source = await readPromptOverlay();

  assert.match(source, /data-selected-target-marker/);
  assert.match(source, /<span className="text-sm leading-none">✓<\/span>/);
  assert.match(source, /<span>已选<\/span>/);
  assert.match(source, /fieldSide === "my" \? "己方" : "对方"/);
  assert.match(source, /` · 第\$\{fieldIndex \+ 1\}位`/);
});

test("手机竖屏旋转画布使用安全区，确认按钮触控高度不小于 44px", async () => {
  const source = await readPromptOverlay();

  assert.match(source, /var\(--layout-safe-left, 0px\)/);
  assert.match(source, /var\(--layout-safe-right, 0px\)/);
  assert.match(source, /var\(--layout-safe-bottom, 0px\)/);
  assert.match(source, /const promptActionHeightClass = rotateQuarterTurn \? "min-h-16" : "min-h-12"/);
  assert.match(source, /\$\{promptActionHeightClass\} rounded-lg bg-orange-500/);
});
