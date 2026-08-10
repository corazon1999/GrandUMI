import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("排行榜页面在同一位置切换 Leader 榜和排位榜", async () => {
  const [panel, sidebar, types, rankedStore] = await Promise.all([
    readSource("../src/components/home/LeaderLeaderboardPanel.tsx"),
    readSource("../src/components/home/MainPanel.tsx"),
    readSource("../src/types/net.ts"),
    readSource("../../服务端WebSocket/Game/Ranked/RankedStore.cs"),
  ]);

  assert.match(panel, /<h2[^>]*>排行榜<\/h2>/);
  assert.match(panel, /setRankingTab\("leader"\)/);
  assert.match(panel, /setRankingTab\("ranked"\)/);
  assert.match(panel, />\s*Leader榜\s*</);
  assert.match(panel, />\s*排位榜\s*</);
  assert.match(panel, /rankingTab === "ranked" \? <RankedLeaderboard items=\{rankLeaderboard\}/);
  assert.match(panel, /RANK_FACTION_NAMES/);
  assert.match(panel, /favoriteLeader/);
  assert.match(panel, />PT</);
  assert.match(types, /favoriteLeader\?: string \| null/);
  assert.match(rankedStore, /GetFavoriteLeaders/);
  assert.match(sidebar, /SidebarButton label="排行榜" icon="leaderboard"/);
});
