import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = await readFile(
  new URL("../src/components/home/LeaderLeaderboardPanel.tsx", import.meta.url),
  "utf8",
);

test("手机窄屏下整个排行榜页面共用纵向滚动容器", () => {
  const leaderboardPanel = source.match(/export default function LeaderLeaderboardPanel\(\)[\s\S]*$/)?.[0];

  assert.ok(leaderboardPanel, "应能找到排行榜页面组件");
  assert.match(leaderboardPanel, /data-testid="leaderboard-page-scroll"/);
  assert.match(
    leaderboardPanel,
    /h-full min-h-0 touch-pan-y overflow-y-auto overscroll-contain p-3 \[-webkit-overflow-scrolling:touch\]/,
  );
  assert.match(leaderboardPanel, /@\[640px\]:flex @\[640px\]:flex-col @\[640px\]:overflow-hidden/);

  const scrollStart = leaderboardPanel.indexOf('data-testid="leaderboard-page-scroll"');
  const heading = leaderboardPanel.indexOf("<header");
  const summary = leaderboardPanel.indexOf("有效对局");
  const leaderboardContent = leaderboardPanel.indexOf("<RankedLeaderboard");
  assert.ok(
    scrollStart < heading && heading < summary && summary < leaderboardContent,
    "标题、页签、统计区和榜单内容应同在手机页面滚动容器内",
  );
});

test("排位榜只在宽屏恢复内部滚动，避免拦截手机页面滑动", () => {
  const rankedLeaderboard = source.match(/function RankedLeaderboard\([\s\S]*?\n}\n\nfunction percent/)?.[0];

  assert.ok(rankedLeaderboard, "应能找到排位榜组件");
  assert.match(rankedLeaderboard, /data-testid="ranked-leaderboard-scroll"/);
  assert.match(
    rankedLeaderboard,
    /overflow-visible @\[640px\]:min-h-0 @\[640px\]:flex-1 @\[640px\]:touch-pan-y @\[640px\]:overflow-y-auto/,
  );

  assert.match(
    source,
    /overflow-clip @\[640px\]:overflow-hidden/,
    "排位榜外框在手机端不应创建嵌套滚动容器",
  );
});

test("Leader 榜只在宽屏使用内部列表滚动", () => {
  assert.match(
    source,
    /overflow-clip @\[640px\]:touch-pan-y @\[640px\]:overflow-auto @\[640px\]:overscroll-contain/,
  );
});
