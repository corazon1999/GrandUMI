import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = await readFile(
  new URL("../src/components/home/ProfilePanel.tsx", import.meta.url),
  "utf8",
);

const rankedInfo = source.match(/<article\s+data-testid="profile-ranked-info"([\s\S]*?)<div className="mt-5 flex flex-col/)?.[1] ?? "";

test("个人详情展示当前赛季排位信息", () => {
  assert.match(source, /const rankProfile = useNetStore\(\(state\) => state\.rankProfile\)/);
  assert.match(rankedInfo, /id="profile-rank-heading"/);
  assert.match(rankedInfo, />排位信息</);
  assert.match(rankedInfo, />当前段位</);
  assert.match(rankedInfo, /RANK_FACTION_NAMES\[rankProfile\.faction\]/);
  assert.match(rankedInfo, /rankProfile\.wins.*胜 \/.*rankProfile\.losses.*负/s);
  assert.match(rankedInfo, /dateLabel\(rankProfile\.seasonEndsAtUtc\)/);
  assert.doesNotMatch(rankedInfo, /排行榜|rankLeaderboard|RP/);
});

test("个人详情排位信息兼容未选阵营和手机竖屏", () => {
  assert.match(rankedInfo, /!rankProfile\.faction/);
  assert.match(rankedInfo, /尚未选择排位阵营/);
  assert.match(rankedInfo, /grid gap-2 @\[560px\]:grid-cols-3/);
  assert.match(rankedInfo, /p-4 @\[720px\]:p-5/);
});
