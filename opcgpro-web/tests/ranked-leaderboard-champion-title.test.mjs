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
  const rankedLeaderboard = panel.match(/function RankedMobileRow\([\s\S]*?\n}\n\nfunction percent/)?.[0];
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

test("排位榜为阵营巅峰与次级称号显示两档专属特效", async () => {
  const [panel, badge, styles] = await Promise.all([
    readSource("../src/components/home/LeaderLeaderboardPanel.tsx"),
    readSource("../src/components/ui/RankTierBadge.tsx"),
    readSource("../src/app/globals.css"),
  ]);

  for (const title of ["海贼王", "海军元帅", "世界之王"]) {
    assert.match(badge, new RegExp(`SUPREME_RANK_TITLES[^;]+${title}`));
  }
  for (const title of ["四皇", "海军大将", "神之骑士团"]) {
    assert.match(badge, new RegExp(`ELITE_RANK_TITLES[^;]+${title}`));
  }
  assert.match(panel, /<RankTierBadge faction=\{item\.faction\}/);
  assert.match(badge, /rank-tier-badge--\$\{effect\}/);
  assert.match(badge, /rank-tier-badge--\$\{faction\}/);
  assert.match(styles, /\.rank-tier-badge--supreme/);
  assert.match(styles, /\.rank-tier-badge--elite/);
  assert.match(styles, /\.rank-tier-badge--pirate[\s\S]+244 63 94/);
  assert.match(styles, /\.rank-tier-badge--marine[\s\S]+14 165 233/);
  assert.match(styles, /\.rank-tier-badge--government[\s\S]+245 158 11/);
  assert.match(styles, /@media \(prefers-reduced-motion: reduce\)[\s\S]+\.rank-tier-badge--supreme/);
});
