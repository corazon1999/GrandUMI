import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = await readFile(
  new URL("../src/components/home/ProfilePanel.tsx", import.meta.url),
  "utf8",
);

test("我的页面展示私有的按领袖个人胜率和完整统计口径", () => {
  assert.match(source, /data-testid="player-leader-win-rates"/);
  assert.match(source, />按领袖个人胜率</);
  assert.match(source, /selectedLeader\.wins.*selectedLeader\.losses/s);
  assert.match(source, /selectedLeader\.winRate/);
  assert.match(source, /selectedLeader\.firstWinRate.*selectedLeader\.secondWinRate/s);
  assert.match(source, /合并休闲\/排位与标准\/狂野/);
  assert.match(source, /好友房、房间码、人机及断线结束不计入/);
  assert.match(source, /min-h-11 rounded-lg px-3/);
});
