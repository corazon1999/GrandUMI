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

test("聊天装饰交易所使用赛季峰值独立莓果额度并提供两个独立自动触发位置", async () => {
  const [panel, protocol, store, rankedStore, bridge, layoutFixture, exchangeLib] = await Promise.all([
    readSource("../src/components/home/MainPanel.tsx"),
    readSource("../src/net/HomeProtocol.ts"),
    readSource("../src/store/netStore.ts"),
    readSource("../../服务端WebSocket/Game/Ranked/RankedStore.cs"),
    readSource("../../服务端WebSocket/WebSocketBridge.cs"),
    readSource("../src/components/game/ChatDecorationLayoutVerification.tsx"),
    readSource("../src/lib/chatDecorationExchange.mjs"),
  ]);

  assert.match(panel, /data-chat-decoration-exchange/);
  assert.match(panel, /SidebarButton label="聊天装饰交易所" icon="exchange"/);
  assert.match(panel, /data-chat-decoration-equip-actions/);
  assert.match(panel, /data-chat-decoration-equip-slot=\{option\.slot\}/);
  assert.match(panel, /min-h-14 w-full rounded-lg/);
  assert.match(panel, /@\[520px\]:grid-cols-2/);
  assert.match(panel, /min-h-14 min-w-\[10rem\]/);
  assert.match(panel, /grid-cols-1 gap-3 @\[620px\]:grid-cols-2 @\[1080px\]:grid-cols-3/);
  assert.match(panel, /snapshot\.balanceBerries < selected\.priceBerries/);
  assert.match(panel, /HomeRequest\.equipChatDecoration\(selected\.id, slot\)/);
  assert.match(panel, /HomeRequest\.purchaseChatDecoration\(selected\.id, selected\.priceBerries\)/);
  assert.match(panel, /永久所有权 · 同一句语录可同时用于开场与胜利/);
  assert.match(panel, /永久拥有后，可分别设为自动开场台词和胜利宣言/);
  assert.match(panel, /本赛季语录额度/);
  assert.doesNotMatch(panel, /balanceRankPoints|priceRankPoints|>RP</);
  assert.match(panel, /orderOwnedChatDecorationItems\(snapshot\?\.items \?\? \[\]\)/);
  assert.match(panel, /!selected\.availableForPurchase/);
  assert.match(panel, /历史藏品 · 已下架/);
  assert.doesNotMatch(panel, /item\.slot === filter/);

  assert.match(protocol, /proto: "MsgChatDecorationExchange"/);
  assert.match(protocol, /CHAT_DECORATION_EXCHANGE_TIMEOUT_MS = 8_000/);
  assert.match(protocol, /lastRequestId !== requestId/);
  assert.match(protocol, /walletMode !== "season_peak_bounty"/);
  assert.match(protocol, /isCurrentChatDecorationPrice\(item\.priceBerries\)/);
  assert.match(protocol, /expectedPriceBerries/);
  assert.doesNotMatch(protocol, /balanceRankPoints|priceRankPoints/);
  assert.match(protocol, /"opening"/);
  assert.match(protocol, /"victory"/);
  assert.doesNotMatch(protocol, /"slot1"/);
  assert.match(protocol, /item\.equippedSlots\.every/);
  assert.match(protocol, /typeof item\.availableForPurchase === "boolean"/);
  assert.match(protocol, /equippedSlots\.size !== equippedSlotCount/);
  assert.ok(
    (protocol.match(/HomeRequest\.requestChatDecorationExchangeSnapshot\(\);/g)?.length ?? 0) >= 2,
    "登录恢复和玩家资料更新都必须重取权威装配快照",
  );
  assert.match(store, /pendingRequestId: string \| null/);
  assert.match(store, /state\.chatDecorationExchange\.lastRequestId !== requestId/);
  assert.match(store, /walletMode: "season_peak_bounty"/);
  assert.doesNotMatch(store, /balanceRankPoints|priceRankPoints/);

  assert.match(rankedStore, /CREATE TABLE IF NOT EXISTS rank_exchange_wallets/);
  assert.match(rankedStore, /BeginTransaction\(deferred: false\)/);
  assert.match(rankedStore, /SynchronizeChatDecorationWalletPeak/);
  assert.match(rankedStore, /credited_peak_rank_points/);
  assert.match(rankedStore, /rank_exchange_wallet_migration_audit/);
  assert.match(rankedStore, /profile_peak_minus_post_faction_selection_purchases/);
  assert.doesNotMatch(rankedStore, /preserve_legacy_max/);
  assert.match(rankedStore, /balance_berries=balance_berries-\$price/);
  assert.match(rankedStore, /PurchasePriceBerries = 50_000_000/);
  assert.match(rankedStore, /chat_decoration_operations/);
  assert.match(rankedStore, /price_berries/);
  assert.match(rankedStore, /MigrateLegacyChatDecorationEquipment/);
  assert.match(rankedStore, /ChatDecorationSlots\.Opening/);
  assert.match(rankedStore, /ChatDecorationSlots\.Victory/);
  assert.match(rankedStore, /quote-pirate-king-man/);
  assert.match(rankedStore, /"我是要成为海贼王的男人!"/);
  assert.match(rankedStore, /AvailableForPurchase = true/);
  assert.match(rankedStore, /definition\.AvailableForPurchase \|\| owned\.Contains/);
  assert.match(rankedStore, /\.Where\(item => item\.Owned\)\s*\.Concat\(visibleItems\.Where\(item => !item\.Owned\)\)/);
  assert.doesNotMatch(rankedStore, /definition\.Slot/);
  assert.match(bridge, /RankedStore\.Default\.PurchaseChatDecoration/);
  assert.match(bridge, /RankedStore\.Default\.EquipChatDecoration/);
  assert.match(bridge, /equippedSlots = item\.EquippedSlots/);
  assert.match(bridge, /availableForPurchase = item\.AvailableForPurchase/);
  assert.match(bridge, /Long\(msg, "expectedPriceBerries", long\.MinValue\)/);
  assert.doesNotMatch(bridge, /balanceRankPoints =|priceRankPoints =/);
  assert.match(bridge, /局内手动发送聊天语录已停用/);
  assert.match(layoutFixture, /exchange-before/);
  assert.match(layoutFixture, /exchange-after/);
  assert.doesNotMatch(layoutFixture, /balanceRankPoints|priceRankPoints/);
  assert.match(exchangeLib, /CHAT_DECORATION_PURCHASE_PRICE_BERRIES = 50_000_000/);
  assert.match(exchangeLib, /MAX_CHAT_DECORATION_WALLET_BERRIES = 9_000_000_000_000_000/);
  assert.match(exchangeLib, /items\.filter\(\(item\) => item\.owned\)/);
  assert.match(exchangeLib, /items\.filter\(\(item\) => !item\.owned\)/);
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
