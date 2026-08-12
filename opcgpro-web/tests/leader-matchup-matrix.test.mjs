import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("Leader 对阵一图流展示排行榜前二十名", async () => {
  const [matrix, store] = await Promise.all([
    readSource("../src/components/home/LeaderMatchupMatrix.tsx"),
    readSource("../../服务端WebSocket/Game/Stats/LeaderStatsStore.cs"),
  ]);

  assert.match(matrix, /const MATRIX_LEADER_LIMIT = 20;/);
  assert.match(matrix, /\.slice\(0, MATRIX_LEADER_LIMIT\)/);
  assert.match(store, /public const int MatchupMatrixLeaderLimit = 20;/);
  assert.match(store, /\.Take\(MatchupMatrixLeaderLimit\)/);
});
