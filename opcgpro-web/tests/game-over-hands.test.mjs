import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("结算时玩家和观战者都显示双方手牌", async () => {
  const source = await readSource("../src/components/game/GameBoard.tsx");

  assert.match(source, /const isGameOver = useGameStore\(\(s\) => s\.isGameOver\)/);
  assert.match(source, /hidden=\{!isPlayback && !revealHands\}/);
  assert.match(source, /hidden=\{isObserver && !revealHands && !revealObserverHand\}/);
  assert.equal((source.match(/revealHands=\{isGameOver\}/g) ?? []).length, 2);
});
