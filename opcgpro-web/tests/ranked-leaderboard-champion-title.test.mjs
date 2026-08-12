import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("排位榜昵称旁显示最强称号且不展示称号胜率", async () => {
  const [panel, types, rankedStore] = await Promise.all([
    readSource("../src/components/home/LeaderLeaderboardPanel.tsx"),
    readSource("../src/types/net.ts"),
    readSource("../../服务端WebSocket/Game/Ranked/RankedStore.cs"),
  ]);
  const rankedLeaderboard = panel.match(/function RankedLeaderboard\([\s\S]*?\n}\n\nfunction percent/)?.[0];
  const rankItem = types.match(/export interface RankLeaderboardItem \{[\s\S]*?\n}/)?.[0];

  assert.ok(rankedLeaderboard, "应能找到排位榜展示组件");
  assert.ok(rankItem, "应能找到排位榜响应类型");
  assert.match(rankedLeaderboard, /LeaderChampionBadgeList/);
  assert.match(rankedLeaderboard, /leaderNumbers=\{item\.championLeaderNumbers\}/);
  assert.match(rankItem, /championLeaderNumbers\?: string\[\]/);
  assert.match(rankedStore, /championLeaderNumbers = value\.ChampionLeaderNumbers/);
  assert.doesNotMatch(rankedLeaderboard, /championWinRate|champion\.winRate/);
  assert.doesNotMatch(rankItem, /championWinRate/);
});
