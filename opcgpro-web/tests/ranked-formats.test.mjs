import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("标准与狂野排位使用独立队列、资料和榜单", async () => {
  const [lobby, leaderboard, store, protocol, types, bridge, roomManager] = await Promise.all([
    readSource("../src/components/home/LobbyPanel.tsx"),
    readSource("../src/components/home/LeaderLeaderboardPanel.tsx"),
    readSource("../src/store/netStore.ts"),
    readSource("../src/net/HomeProtocol.ts"),
    readSource("../src/types/net.ts"),
    readSource("../../服务端WebSocket/WebSocketBridge.cs"),
    readSource("../../服务端WebSocket/Game/GameRoomManager.cs"),
  ]);

  assert.match(types, /RankedMode = "standard" \| "wild"/);
  assert.match(types, /MatchQueueKind = "ranked" \| "rankedWild" \| "casual"/);
  assert.match(store, /rankProfiles: Record<RankedMode/);
  assert.match(store, /rankLeaderboards: Record<RankedMode/);
  assert.match(protocol, /requestRankSnapshot\(mode: RankedMode = "standard"\)/);
  assert.match(lobby, /aria-label="排位模式"/);
  assert.match(lobby, /setMatchQueueKind\("rankedWild"\)/);
  assert.match(leaderboard, /useState<RankedMode>\("standard"\)/);
  assert.match(leaderboard, /aria-label="排位榜模式"/);
  assert.match(bridge, /WildRankedMatchQueue/);
  assert.match(bridge, /DeckValidator\.FormatStandard/);
  assert.match(roomManager, /MatchKind\.RankedWild/);
});

test("标准排位默认展示且狂野排位允许标准禁限卡", async () => {
  const [validator, cardInfo, cardDatabase, rankedStore] = await Promise.all([
    readSource("../../服务端WebSocket/Game/Validation/DeckValidator.cs"),
    readSource("../../服务端WebSocket/Cards/CardInfo.cs"),
    readSource("../../服务端WebSocket/Cards/CardDatabase.cs"),
    readSource("../../服务端WebSocket/Game/Ranked/RankedStore.cs"),
  ]);

  assert.match(validator, /FormatStandard = "Standard"/);
  assert.match(validator, /StandardLegalSubscriptOneCards/);
  assert.match(validator, /card\.Subscript == 1/);
  assert.match(cardInfo, /public int Subscript/);
  assert.match(cardDatabase, /Subscript\s*= ParseSubscript\(r\.subscript\)/);
  assert.match(rankedStore, /ranked-wild\.db/);
  assert.match(rankedStore, /GRANDUMI_RANKED_WILD_DB/);
});

test("排位模式切换在手机竖屏保持可见且触控区域合格", async () => {
  const [lobby, leaderboard] = await Promise.all([
    readSource("../src/components/home/LobbyPanel.tsx"),
    readSource("../src/components/home/LeaderLeaderboardPanel.tsx"),
  ]);

  assert.match(lobby, /overflow-y-auto px-4 py-3/);
  assert.match(lobby, /\[@media\(max-height:800px\)\]:hidden/);
  assert.match(lobby, /aria-label="排位模式"/);
  assert.match(lobby, /min-h-11 rounded-lg px-3 text-sm font-black/);
  assert.match(leaderboard, /aria-label="排位榜模式"/);
  assert.match(leaderboard, /min-h-11 rounded-md px-4/);
});
