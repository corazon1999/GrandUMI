import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("选择咚后点击牌桌空白处会取消待依附操作", async () => {
  const [board, cardItem] = await Promise.all([
    readSource("../src/components/game/GameBoard.tsx"),
    readSource("../src/components/ui/CardItem.tsx"),
  ]);

  assert.match(board, /const selectedDonIndex = useGameStore/);
  assert.match(board, /const handleBoardBlankClick =/);
  assert.match(board, /selectedDonIndex === null/);
  assert.match(board, /event\.target\.closest\(/);
  assert.match(board, /\[data-game-board-interactive='true'\]/);
  assert.match(board, /if \(interactiveTarget\) return;/);
  assert.match(board, /setSelectedDon\(null\);/);
  assert.match(board, /onClick=\{handleBoardBlankClick\}/);
  assert.match(cardItem, /data-game-board-interactive=\{onClick \? "true" : undefined\}/);
});
