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
  assert.doesNotMatch(matrix, /\.slice\(0,\s*12\)/);
  assert.doesNotMatch(exporter, /\.slice\(0,\s*12\)/);
});

test("矩阵每格按纵轴行玩家视角展示综合和先后手统计", async () => {
  const [matrix, types, bridge] = await Promise.all([
    readSource("../src/components/home/LeaderMatchupMatrix.tsx"),
    readSource("../src/types/net.ts"),
    readSource("../../服务端WebSocket/WebSocketBridge.cs"),
  ]);

  assert.match(types, /interface LeaderMatchupItem[\s\S]*?firstGames: number;[\s\S]*?firstWins: number;[\s\S]*?firstLosses: number;[\s\S]*?firstWinRate: number \| null;/);
  assert.match(types, /interface LeaderMatchupItem[\s\S]*?secondGames: number;[\s\S]*?secondWins: number;[\s\S]*?secondLosses: number;[\s\S]*?secondWinRate: number \| null;/);
  assert.match(bridge, /firstGames = x\.FirstGames,[\s\S]*?firstWins = x\.FirstWins,[\s\S]*?firstLosses = x\.FirstLosses/);
  assert.match(bridge, /secondGames = x\.SecondGames,[\s\S]*?secondWins = x\.SecondWins,[\s\S]*?secondLosses = x\.SecondLosses/);
  assert.match(matrix, /总 \$\{item\.games\}场/);
  assert.match(matrix, /positionText\("先", item\?\.firstGames, item\?\.firstWinRate\)/);
  assert.match(matrix, /positionText\("后", item\?\.secondGames, item\?\.secondWinRate\)/);
  assert.match(matrix, /positionDetailText\("先", item\?\.firstGames, item\?\.firstWins, item\?\.firstWinRate\)/);
  assert.match(matrix, /positionDetailText\("后", item\?\.secondGames, item\?\.secondWins, item\?\.secondWinRate\)/);
  assert.match(matrix, /先\/后均按纵轴我方视角/);
  assert.match(matrix, /榜前 \{LEADER_MATCHUP_MATRIX_LIMIT\}/);

  const visiblePositionText = matrix.slice(
    matrix.indexOf("function positionText"),
    matrix.indexOf("function positionDetailText"),
  );
  assert.doesNotMatch(visiblePositionText, /胜\//, "格内先后手短文案不得拼接胜场，避免大样本撑宽");
});
