import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = await readFile(
  new URL("../src/components/home/LeaderLeaderboardPanel.tsx", import.meta.url),
  "utf8",
);

test("手机窄屏下排位榜筛选区和玩家列表共用纵向滚动容器", () => {
  const rankedLeaderboard = source.match(/function RankedLeaderboard\([\s\S]*?\n}\n\nfunction percent/)?.[0];

  assert.ok(rankedLeaderboard, "应能找到排位榜组件");
  assert.match(rankedLeaderboard, /data-testid="ranked-leaderboard-scroll"/);
  assert.match(
    rankedLeaderboard,
    /min-h-0 flex-1 touch-pan-y overflow-y-auto overscroll-contain \[-webkit-overflow-scrolling:touch\]/,
  );

  const scrollStart = rankedLeaderboard.indexOf('data-testid="ranked-leaderboard-scroll"');
  const factionFilters = rankedLeaderboard.indexOf("全服个人榜");
  const playerRows = rankedLeaderboard.indexOf("topItems.map");
  assert.ok(scrollStart < factionFilters && factionFilters < playerRows, "筛选区和玩家行应同在滚动容器内");
});

test("Leader 榜的窄屏列表也显式启用纵向触摸滚动", () => {
  assert.match(
    source,
    /min-h-0 flex-1 touch-pan-y overscroll-contain rounded-xl[\s\S]*?\[-webkit-overflow-scrolling:touch\]/,
  );
});
