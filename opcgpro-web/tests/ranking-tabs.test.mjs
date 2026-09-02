import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { calculateLayoutScale } from "../src/lib/gameLayout.ts";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("排行榜页面在同一位置切换 Leader 榜和排位榜", async () => {
  const [panel, sidebar, types, rankedStore] = await Promise.all([
    readSource("../src/components/home/LeaderLeaderboardPanel.tsx"),
    readSource("../src/components/home/MainPanel.tsx"),
    readSource("../src/types/net.ts"),
    readSource("../../服务端WebSocket/Game/Ranked/RankedStore.cs"),
  ]);

  assert.match(panel, /<h2[^>]*>排行榜<\/h2>/);
  assert.match(panel, /setRankingTab\("leader"\)/);
  assert.match(panel, /setRankingTab\("ranked"\)/);
  assert.match(panel, /useState<"leader" \| "ranked">\("ranked"\)/);
  assert.ok(panel.indexOf(">\n              排位榜\n") < panel.indexOf(">\n              Leader榜\n"), "排位榜页签应位于 Leader 榜左侧");
  assert.match(panel, />\s*Leader榜\s*</);
  assert.match(panel, />\s*排位榜\s*</);
  assert.match(panel, /rankingTab === "ranked" \? rankProfile \? <RankedLeaderboard items=\{rankLeaderboard\} standings=\{factionStandings\}/);
  assert.match(panel, /useState<RankedMode>\("standard"\)/);
  assert.match(panel, /aria-label="排位榜模式"/);
  assert.match(panel, /RANK_FACTION_NAMES/);
  assert.match(panel, /favoriteLeader/);
  assert.match(panel, />悬赏金</);
  assert.match(panel, /formatRankBounty\(item\.rankPoints\)/);
  assert.match(types, /favoriteLeader\?: string \| null/);
  assert.match(rankedStore, /GetFavoriteLeaders/);
  assert.match(sidebar, /SidebarButton label="排行榜" icon="leaderboard"/);
});

test("聊天装饰交易所使用标准排位权威余额并覆盖窄屏购买装配状态", async () => {
  const [panel, protocol, store, rankedStore, bridge] = await Promise.all([
    readSource("../src/components/home/MainPanel.tsx"),
    readSource("../src/net/HomeProtocol.ts"),
    readSource("../src/store/netStore.ts"),
    readSource("../../服务端WebSocket/Game/Ranked/RankedStore.cs"),
    readSource("../../服务端WebSocket/WebSocketBridge.cs"),
  ]);

  assert.match(panel, /data-chat-decoration-exchange/);
  assert.match(panel, /SidebarButton label="聊天装饰交易所" icon="exchange"/);
  assert.match(panel, /min-h-14 min-w-\[4\.5rem\]/);
  assert.match(panel, /min-h-14 min-w-\[10rem\]/);
  assert.match(panel, /grid-cols-1 gap-3 @\[620px\]:grid-cols-2 @\[1080px\]:grid-cols-3/);
  assert.match(panel, /snapshot\.balanceRankPoints < selected\.priceRankPoints/);
  assert.match(panel, /selected\.owned\) HomeRequest\.equipChatDecoration/);
  assert.match(panel, /HomeRequest\.purchaseChatDecoration/);
  assert.match(panel, /永久所有权 · 每类槽位同时装配一个/);

  assert.match(protocol, /proto: "MsgChatDecorationExchange"/);
  assert.match(protocol, /CHAT_DECORATION_EXCHANGE_TIMEOUT_MS = 8_000/);
  assert.match(protocol, /lastRequestId !== requestId/);
  assert.match(protocol, /walletMode !== "standard"/);
  assert.ok(
    (protocol.match(/HomeRequest\.requestChatDecorationExchangeSnapshot\(\);/g)?.length ?? 0) >= 2,
    "登录恢复和玩家资料更新都必须重取权威装配快照",
  );
  assert.match(store, /pendingRequestId: string \| null/);
  assert.match(store, /state\.chatDecorationExchange\.lastRequestId !== requestId/);

  assert.match(rankedStore, /CREATE TABLE IF NOT EXISTS rank_exchange_wallets/);
  assert.match(rankedStore, /BeginTransaction\(deferred: false\)/);
  assert.match(rankedStore, /ApplyChatDecorationWalletSettlementDelta/);
  assert.match(rankedStore, /chat_decoration_operations/);
  assert.match(bridge, /RankedStore\.Default\.PurchaseChatDecoration/);
  assert.match(bridge, /RankedStore\.Default\.EquipChatDecoration/);
});

test("两档手机竖屏的交易所主要控件缩放后仍至少为 44 像素", () => {
  for (const [hostWidth, hostHeight] of [[390, 844], [360, 780]]) {
    const scale = calculateLayoutScale({
      hostWidth,
      hostHeight,
      canvasWidth: 390,
      canvasHeight: 844,
      rotateQuarterTurn: false,
      edgeToEdge: false,
    });

    assert.ok(56 * scale >= 44, `${hostWidth}×${hostHeight} 的交易所触控尺寸不足 44px`);
  }
});
