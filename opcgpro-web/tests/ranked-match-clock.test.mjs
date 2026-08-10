import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("公开匹配明确保留排位与休闲两个入口", async () => {
  const [lobby, protocol, types] = await Promise.all([
    readSource("../src/components/home/LobbyPanel.tsx"),
    readSource("../src/net/HomeProtocol.ts"),
    readSource("../src/types/net.ts"),
  ]);

  assert.match(lobby, />\s*排位匹配\s*</);
  assert.match(lobby, />\s*休闲匹配\s*</);
  assert.match(lobby, /setMatchQueueKind\("ranked"\)/);
  assert.match(lobby, /setMatchQueueKind\("casual"\)/);
  assert.match(protocol, /enterMatch\(deck: string, deckName\?: string, queueKind: "ranked" \| "casual" = "casual"\)/);
  assert.match(types, /queueKind\?: "ranked" \| "casual"/);
});

test("排位前必须选择阵营，更换阵营须确认并清空排位进度", async () => {
  const [lobby, protocol, types, rankedStore] = await Promise.all([
    readSource("../src/components/home/LobbyPanel.tsx"),
    readSource("../src/net/HomeProtocol.ts"),
    readSource("../src/types/net.ts"),
    readSource("../../服务端WebSocket/Game/Ranked/RankedStore.cs"),
  ]);

  assert.match(lobby, /选择你的排位阵营/);
  assert.match(lobby, /HomeRequest\.selectRankFaction\(pendingFaction, true\)/);
  assert.match(lobby, /确认更换并清空/);
  assert.match(lobby, /更换后将清空本赛季 RP、定级进度和战绩/);
  assert.match(lobby, /Boolean\(rankProfile\?\.faction\)/);
  assert.match(protocol, /selectRankFaction\(faction: RankFaction, resetRankProgress = false\)/);
  assert.match(protocol, /resetRankProgress/);
  assert.match(types, /type RankFaction = "pirate" \| "marine" \| "government"/);
  assert.match(rankedStore, /INSERT INTO rank_factions/);
  assert.match(rankedStore, /更换阵营会重置当前赛季的排位进度/);
  assert.match(rankedStore, /ResetRankProgress/);
});

test("三阵营称号和新世界榜首称号按约定映射", async () => {
  const rankedStore = await readSource("../../服务端WebSocket/Game/Ranked/RankedStore.cs");

  for (const label of ["见习海贼", "海贼战斗员", "海贼干部", "副船长", "船长", "海军三等兵", "海军少尉", "海军少校", "海军少将", "海军中将", "政府线人", "初级特工", "CP9 特工", "CP0 特工", "神之骑士团", "新世界", "海贼王", "四皇", "海军元帅", "海军大将", "世界之王", "五老星"]) {
    assert.match(rankedStore, new RegExp(label));
  }
});

test("对局界面展示双方独立的权威操作棋钟", async () => {
  const [board, store, netTypes] = await Promise.all([
    readSource("../src/components/game/GameBoard.tsx"),
    readSource("../src/store/gameStore.ts"),
    readSource("../src/types/net.ts"),
  ]);

  assert.match(board, /<OperationClock side="opponent" \/>/);
  assert.match(board, /<OperationClock side="my" \/>/);
  assert.match(store, /s\.myOperationTimeMs = msg\.myOperationTimeMs \?\? 1_200_000/);
  assert.match(store, /s\.opponentOperationTimeMs = msg\.opponentOperationTimeMs \?\? 1_200_000/);
  assert.match(netTypes, /operationClockActive\?: "my" \| "opponent" \| null/);
});

test("休闲公开匹配也使用双方各二十分钟的操作棋钟", async () => {
  const manager = await readSource("../../服务端WebSocket/Game/GameRoomManager.cs");

  assert.match(manager, /private const long OperationTimeLimitMs = 20 \* 60 \* 1000/);
  assert.match(manager, /OperationClockEnabled = matchKind is MatchKind\.Ranked or MatchKind\.Casual or MatchKind\.Matchmaking/);
  assert.match(manager, /OperationClockRemainingMs\[0\] = OperationTimeLimitMs/);
  assert.match(manager, /OperationClockRemainingMs\[1\] = OperationTimeLimitMs/);
});

test("断线提示只展示服务端两分钟宽限且不能提前判负", async () => {
  const [banner, manager] = await Promise.all([
    readSource("../src/components/game/OpponentDisconnectBanner.tsx"),
    readSource("../../服务端WebSocket/Game/GameRoomManager.cs"),
  ]);

  assert.match(banner, /setCountdown\(payload\.gracePeriodSeconds\)/);
  assert.match(banner, /每名玩家每局累计 120 秒宽限/);
  assert.doesNotMatch(banner, /GameRequest/);
  assert.match(manager, /private const int GracePeriodSeconds = 120/);
  assert.match(manager, /DisconnectGraceRemainingMs/);
  assert.match(manager, /对手仍在 2 分钟断线宽限期内/);
});
