export const CHAT_DECORATION_PURCHASE_PRICE_BERRIES = 50_000_000;
export const MAX_CHAT_DECORATION_WALLET_BERRIES = 9_000_000_000_000_000;

/**
 * 协议里的莓果金额必须能由 JavaScript 精确表达，并与服务端账本边界一致。
 *
 * @param {unknown} value
 * @param {boolean} [allowZero]
 */
export function isValidChatDecorationBerryAmount(value, allowZero = true) {
  return Number.isSafeInteger(value)
    && value >= (allowZero ? 0 : 1)
    && value <= MAX_CHAT_DECORATION_WALLET_BERRIES;
}

/**
 * @param {unknown} value
 */
export function isCurrentChatDecorationPrice(value) {
  return isValidChatDecorationBerryAmount(value, false)
    && value === CHAT_DECORATION_PURCHASE_PRICE_BERRIES;
}

/**
 * 已拥有条目整体前置；两个分组都保留服务端目录顺序。
 *
 * @template {{ owned: boolean }} T
 * @param {readonly T[]} items
 * @returns {T[]}
 */
export function orderOwnedChatDecorationItems(items) {
  return [
    ...items.filter((item) => item.owned),
    ...items.filter((item) => !item.owned),
  ];
}
