import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("终局快照向双方公开当前手牌", async () => {
  const source = await readSource("../../服务端WebSocket/Game/Snapshot/StateSnapshotBuilder.cs");

  assert.match(source, /asSelf: !isSpectator,[\s\S]*?revealHand: state\.IsGameOver/);
  assert.match(source, /asSelf: false,[\s\S]*?revealHand: state\.IsGameOver/);
});

test("终局牌桌显示双方手牌并允许隐藏和恢复结算面板", async () => {
  const [overlay, board] = await Promise.all([
    readSource("../src/components/game/GameOverOverlay.tsx"),
    readSource("../src/components/game/GameBoard.tsx"),
  ]);

  assert.match(overlay, /const \[hidden, setHidden\]/);
  assert.match(overlay, />\s*查看牌桌\s*</);
  assert.match(overlay, />\s*查看结算\s*</);
  assert.equal(overlay.match(/h-12 min-w-28/g)?.length, 3);
  assert.match(overlay, /var\(--layout-safe-right/);
  assert.match(overlay, /var\(--layout-safe-bottom/);
  assert.equal(board.match(/revealHands=\{isGameOver\}/g)?.length, 2);
});
