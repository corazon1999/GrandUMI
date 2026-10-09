import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

test("排位榜用图标展示四海击败数量并可切换海域内部排名", async () => {
  const source = await readFile(new URL("../src/components/home/LeaderLeaderboardPanel.tsx", import.meta.url), "utf8");
  assert.match(source, /factionStandingsByMode/);
  assert.match(source, /HunterDefeatCount value=\{standing\.totalRankPoints\}/);
  assert.match(source, /item\.factionRank <= 100/);
  assert.match(source, /击败结算前非零领先海域玩家，击败数量额外 \+1/);
  assert.match(source, /min-h-11/);
});
