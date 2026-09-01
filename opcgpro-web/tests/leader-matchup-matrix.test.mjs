import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("Leader 对阵一图流展示排行榜前二十名", async () => {
  const [matrix, exporter, store] = await Promise.all([
    readSource("../src/components/home/LeaderMatchupMatrix.tsx"),
    readSource("../src/lib/leaderMatchupMatrixExport.ts"),
    readSource("../../服务端WebSocket/Game/Stats/LeaderStatsStore.cs"),
  ]);

  assert.match(matrix, /selectLeaderMatchupMatrixLeaders\(leaderboardItems\)/);
  assert.match(exporter, /const LEADER_MATCHUP_MATRIX_LIMIT = 20;/);
  assert.match(exporter, /\.slice\(0, LEADER_MATCHUP_MATRIX_LIMIT\)/);
  assert.match(store, /public const int MatchupMatrixLeaderLimit = 20;/);
  assert.match(store, /\.Take\(MatchupMatrixLeaderLimit\)/);
});
