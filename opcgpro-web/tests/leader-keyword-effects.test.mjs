import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

test("领袖卡接收快照动态关键词并启用关键词图标层", async () => {
  const leaderCard = await readFile(
    new URL("../src/components/game/LeaderCard.tsx", import.meta.url),
    "utf8",
  );

  assert.match(leaderCard, /<CardItem[\s\S]*?showKeywordFx/);
  assert.match(leaderCard, /gainedKeywords=\{player\.leaderGainedKeywords\}/);
});

test("旧回放缺少领袖动态关键词时回退为空数组", async () => {
  const gameStore = await readFile(
    new URL("../src/store/gameStore.ts", import.meta.url),
    "utf8",
  );

  assert.match(
    gameStore,
    /leaderGainedKeywords:\s*\[\.\.\.\(player\.leaderGainedKeywords \?\? \[\]\)\]/,
  );
});
