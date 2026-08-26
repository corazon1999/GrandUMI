/**
 * 只在牌桌已经按当前主视角归一化为 opponent 时替换卡背。
 * 未提供 side 的个人资料、卡背广场等预览保持原样。
 *
 * @param {string | null | undefined} cardBackId
 * @param {"my" | "opponent" | null | undefined} side
 * @param {boolean} hideOpponentCardBack
 * @param {string} defaultCardBackId
 * @returns {string | null | undefined}
 */
export function resolveVisibleCardBackId(
  cardBackId,
  side,
  hideOpponentCardBack,
  defaultCardBackId,
) {
  return hideOpponentCardBack && side === "opponent"
    ? defaultCardBackId
    : cardBackId;
}
