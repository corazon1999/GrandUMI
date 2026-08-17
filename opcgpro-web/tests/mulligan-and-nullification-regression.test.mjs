import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";

const root = process.cwd();

test("对局结束后调度遮罩立即卸载", async () => {
  const source = await readFile(
    path.join(root, "src/components/game/MulliganOverlay.tsx"),
    "utf8",
  );

  assert.match(source, /const isGameOver = useGameStore\(\(s\) => s\.isGameOver\)/);
  assert.match(source, /if \(isGameOver\) return null;/);
  assert.match(source, /useServerCountdown/);
  assert.doesNotMatch(source, /disabled=\{timedOut \|\| isPending\}/);
  assert.doesNotMatch(source, /if \(timedOut \|\| useGameStore\.getState\(\)\.isPending\) return/);
});

test("角色效果无效状态会显示在牌桌上", async () => {
  const [fieldArea, gameStore] = await Promise.all([
    readFile(path.join(root, "src/components/game/FieldArea.tsx"), "utf8"),
    readFile(path.join(root, "src/store/gameStore.ts"), "utf8"),
  ]);

  assert.match(gameStore, /effectsNullified: card\.effectsNullified \?\? false/);
  assert.match(fieldArea, /fc\.effectsNullified/);
  assert.match(fieldArea, />\s*效果无效\s*</);
});

test("获得可攻击活跃后，前端允许点选活跃角色", async () => {
  const fieldArea = await readFile(
    path.join(root, "src/components/game/FieldArea.tsx"),
    "utf8",
  );

  assert.match(fieldArea, /attackerCanAttackActive/);
  assert.match(fieldArea, /fc\.isTapped \|\| attackerCanAttackActive/);
  assert.match(fieldArea, /if \(!isTapped && !attackerCanAttackActive\) return/);
});
