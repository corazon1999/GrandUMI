import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = await readFile(
  new URL("../src/components/game/GameActions.tsx", import.meta.url),
  "utf8",
);

test("结束回合与普通操作区保持视觉隔离", () => {
  assert.match(source, /回合控制/);
  assert.match(source, /border-t border-rose-200\/20 pt-4/);
});

test("每次结束回合都先进入确认状态", () => {
  assert.match(
    source,
    /const requestEndTurn = \(\) => \{\s*setIsEndTurnConfirming\(true\);\s*\};/,
  );
  const requestEndTurn = source.match(
    /const requestEndTurn = \(\) => \{([\s\S]*?)\n  \};/,
  );
  assert.ok(requestEndTurn);
  assert.doesNotMatch(requestEndTurn[1], /endTurn\(/);
});

test("确认操作可取消且会在三秒后自动恢复", () => {
  assert.match(source, /window\.setTimeout\(\(\) => setIsEndTurnConfirming\(false\), 3_000\)/);
  assert.match(source, /确定结束回合？/);
  assert.match(source, /grid grid-cols-2 gap-2/);
  assert.match(source, />\s*取消\s*</);
  assert.match(source, />\s*确认结束\s*</);
});
