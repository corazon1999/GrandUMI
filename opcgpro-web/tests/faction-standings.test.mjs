import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

test("排位榜展示阵营总分并可切换阵营内部排名", async () => {
  const source = await readFile(new URL("../src/components/home/LeaderLeaderboardPanel.tsx", import.meta.url), "utf8");
  assert.match(source, /factionStandingsByMode/);
  assert.match(source, /总分 \{standing\.totalRankPoints\.toLocaleString\(\)\}/);
  assert.match(source, /item\.factionRank <= 100/);
  assert.match(source, /点击阵营可查看内部排行榜/);
  assert.match(source, /min-h-11/);
});
