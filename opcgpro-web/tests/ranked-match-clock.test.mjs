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
  assert.match(lobby, /更换后将清空本赛季悬赏金、定级进度和战绩/);
  assert.match(lobby, /Boolean\(rankProfile\?\.faction\)/);
  assert.match(protocol, /selectRankFaction\(faction: RankFaction, resetRankProgress = false\)/);
  assert.match(protocol, /resetRankProgress/);
  assert.match(types, /type RankFaction = "pirate" \| "marine" \| "government"/);
  assert.match(rankedStore, /INSERT INTO rank_factions/);
  assert.match(rankedStore, /更换阵营会重置当前赛季的排位进度/);
  assert.match(rankedStore, /ResetRankProgress/);
});

test("排位卡片只显示当前段位并可展开阵营规则", async () => {
  const [lobby, types, rankedStore] = await Promise.all([
    readSource("../src/components/home/LobbyPanel.tsx"),
    readSource("../src/types/net.ts"),
    readSource("../../服务端WebSocket/Game/Ranked/RankedStore.cs"),
  ]);

  assert.doesNotMatch(lobby, /查看排位榜/);
  assert.doesNotMatch(lobby, /rankLeaderboard/);
  assert.doesNotMatch(lobby, /见习海贼 → 船长/);
  assert.match(lobby, /当前段位/);
  assert.match(lobby, /<RankTierBadge faction=\{rankProfile\.faction\}/);
  assert.match(lobby, /<LeaderChampionBadgeList leaderNumbers=\{rankProfile\.championLeaderNumbers\}/);
  assert.match(types, /interface RankProfileSnapshot[\s\S]+championLeaderNumbers\?: string\[\]/);
  assert.match(rankedStore, /championLeaderNumbers = value\.ChampionLeaderNumbers/);
  assert.match(lobby, /aria-label="排位阵营操作"/);
  assert.match(lobby, /aria-expanded=\{rankRulesOpen\}/);
  assert.match(lobby, />\s*阵营规则\s*</);
  assert.match(lobby, /先完成 5 场定级赛/);
  assert.match(lobby, /悬赏金每增加 1000万贝里变化一个小段，每增加 3000万贝里进入下一称号/);
  assert.match(lobby, /悬赏金未达到 1亿5000万贝里时，基础胜负会增加或减少 200万贝里/);
  assert.match(lobby, /11 连胜起奖励封顶 100万贝里/);
  assert.match(lobby, /6 连败起保护封顶 50万贝里/);
  assert.match(lobby, /低悬赏方每低 1000万贝里多加或少扣 10万贝里/);
  assert.match(lobby, /高悬赏方每高 1000万贝里少加或多扣 10万贝里/);
  assert.match(lobby, /达到 1亿5000万但未达到 10亿贝里时/);
  assert.match(lobby, /基础胜负变为增加或减少 400万贝里/);
  assert.match(lobby, /连胜奖励、连败保护和分差修正上限全部翻倍/);
  assert.match(lobby, /分别最高为 200万、100万和 100万贝里/);
  assert.match(lobby, /达到 10亿贝里后再次翻倍/);
  assert.match(lobby, /基础胜负增加或减少 800万贝里/);
  assert.match(lobby, /分别最高为 400万、200万和 200万贝里/);
});

test("排位结算逐项展示基础分、连续场次、分差和保护修正", async () => {
  const [page, panel, types] = await Promise.all([
    readSource("../src/app/game/page.tsx"),
    readSource("../src/components/game/RankResultPanel.tsx"),
    readSource("../src/types/net.ts"),
  ]);

  assert.match(page, /<RankResultPanel result=\{rankResult\}/);
  assert.match(panel, /data-testid="rank-rp-breakdown"/);
  assert.match(panel, /baseRankPointDelta/);
  assert.match(panel, /streakAdjustment/);
  assert.match(panel, /rankDifferenceAdjustment/);
  assert.match(panel, /formatSignedRankBounty\(result\.rankPointDelta\)/);
  assert.match(panel, /悬赏金\{formatSignedRankBounty\(result\.rankPointDelta\)\}/);
  assert.doesNotMatch(panel, />.*RP| RP/);
  assert.match(panel, /低悬赏方获胜奖励/);
  assert.match(panel, /低悬赏方失败保护/);
  assert.match(panel, /高悬赏方获胜削减/);
  assert.match(panel, /高悬赏方失败追加扣除/);
  assert.match(panel, /rankProtectionAdjustment/);
  assert.match(panel, /最终变化/);
  assert.match(page, /min-h-11/);
  assert.match(types, /rankPointFormulaApplied: boolean/);
});

test("三阵营称号和高悬赏称号按约定映射", async () => {
  const rankedStore = await readSource("../../服务端WebSocket/Game/Ranked/RankedStore.cs");

  for (const label of ["见习海贼", "海贼战斗员", "海贼干部", "副船长", "船长", "海军三等兵", "海军少尉", "海军少校", "海军少将", "海军中将", "政府线人", "初级特工", "CP9 特工", "CP0 特工", "浅海契约", "超新星", "大将候补", "神之骑士团", "海贼王", "四皇", "海军元帅", "海军大将", "世界之王", "五老星"]) {
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

test("排位对局右上角展示双方阵营和段位", async () => {
  const [board, store, netTypes, manager, snapshotBuilder] = await Promise.all([
    readSource("../src/components/game/GameBoard.tsx"),
    readSource("../src/store/gameStore.ts"),
    readSource("../src/types/net.ts"),
    readSource("../../服务端WebSocket/Game/GameRoomManager.cs"),
    readSource("../../服务端WebSocket/Game/Snapshot/StateSnapshotBuilder.cs"),
  ]);

  assert.match(board, /<PlayerRankIdentity rank=\{opponentRankIdentity\} \/>/);
  assert.match(board, /<PlayerRankIdentity rank=\{myRankIdentity\} \/>/);
  assert.match(board, /定级 \$\{rank\.placementGames\}\/\$\{rank\.placementRequired\}/);
  assert.match(board, /海贼/);
  assert.match(board, /海军/);
  assert.match(board, /世界政府/);
  assert.match(store, /rankIdentity\?: PlayerRankIdentitySnapshot \| null/);
  assert.match(netTypes, /rankIdentity\?: PlayerRankIdentitySnapshot \| null/);
  assert.match(manager, /AttachRankIdentities\(engine\.State, matchKind/);
  assert.match(snapshotBuilder, /rankIdentity = state\.MatchKind == MatchKind\.Ranked/);
});

test("休闲公开匹配也使用双方各二十分钟的操作棋钟", async () => {
  const manager = await readSource("../../服务端WebSocket/Game/GameRoomManager.cs");

  assert.match(manager, /private const long OperationTimeLimitMs = 20 \* 60 \* 1000/);
  assert.match(manager, /OperationClockEnabled = matchKind is MatchKind\.Ranked or MatchKind\.Casual or MatchKind\.Matchmaking/);
  assert.match(manager, /OperationClockRemainingMs\[0\] = OperationTimeLimitMs/);
  assert.match(manager, /OperationClockRemainingMs\[1\] = OperationTimeLimitMs/);
});

test("断线提示只展示服务端九十秒宽限且不能提前判负", async () => {
  const [banner, manager] = await Promise.all([
    readSource("../src/components/game/OpponentDisconnectBanner.tsx"),
    readSource("../../服务端WebSocket/Game/GameRoomManager.cs"),
  ]);

  assert.match(banner, /setCountdown\(payload\.gracePeriodSeconds\)/);
  assert.match(banner, /每名玩家每局累计 90 秒宽限/);
  assert.doesNotMatch(banner, /GameRequest/);
  assert.match(manager, /private const int GracePeriodSeconds = 90/);
  assert.match(manager, /DisconnectGraceRemainingMs/);
  assert.match(manager, /对手仍在 90 秒断线宽限期内/);
});
