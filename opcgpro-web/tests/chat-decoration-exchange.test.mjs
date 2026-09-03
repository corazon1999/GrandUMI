import assert from "node:assert/strict";
import test from "node:test";
import {
  CHAT_DECORATION_PURCHASE_PRICE_BERRIES,
  MAX_CHAT_DECORATION_WALLET_BERRIES,
  isCurrentChatDecorationPrice,
  isValidChatDecorationBerryAmount,
  orderOwnedChatDecorationItems,
} from "../src/lib/chatDecorationExchange.mjs";

test("交易所莓果字段只接受与服务端一致的安全整数边界", () => {
  assert.equal(CHAT_DECORATION_PURCHASE_PRICE_BERRIES, 50_000_000);
  assert.equal(isCurrentChatDecorationPrice(50_000_000), true);
  assert.equal(isCurrentChatDecorationPrice(4_500_000), false);
  assert.equal(isCurrentChatDecorationPrice(50_000_000.5), false);
  assert.equal(isCurrentChatDecorationPrice("50000000"), false);

  assert.equal(isValidChatDecorationBerryAmount(0), true);
  assert.equal(isValidChatDecorationBerryAmount(MAX_CHAT_DECORATION_WALLET_BERRIES), true);
  assert.equal(isValidChatDecorationBerryAmount(-1), false);
  assert.equal(isValidChatDecorationBerryAmount(Number.MAX_SAFE_INTEGER), false);
  assert.equal(isValidChatDecorationBerryAmount(MAX_CHAT_DECORATION_WALLET_BERRIES + 100_000), false);
  assert.equal(isValidChatDecorationBerryAmount(Number.NaN), false);
});

test("已拥有条目整体前置且购买前后各组保持目录顺序", () => {
  const before = [
    { id: "catalog-1", owned: false },
    { id: "legacy-owned", owned: true },
    { id: "catalog-2", owned: false },
    { id: "equipped-owned", owned: true },
    { id: "catalog-3", owned: false },
  ];
  assert.deepEqual(
    orderOwnedChatDecorationItems(before).map((item) => item.id),
    ["legacy-owned", "equipped-owned", "catalog-1", "catalog-2", "catalog-3"],
  );

  const afterPurchase = before.map((item) => item.id === "catalog-2"
    ? { ...item, owned: true }
    : item);
  assert.deepEqual(
    orderOwnedChatDecorationItems(afterPurchase).map((item) => item.id),
    ["legacy-owned", "catalog-2", "equipped-owned", "catalog-1", "catalog-3"],
  );
});
