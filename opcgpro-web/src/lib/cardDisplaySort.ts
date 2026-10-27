/** 只返回展示下标，保留服务器数组和同一卡牌实例的原始位置。 */
export function sortCardDisplayIndices(
  numbers: readonly string[],
  getCost: (number: string) => number | undefined,
): number[] {
  // 统一使用卡面费用，避免临时减费把相同卡号拆开；资料缺失的卡排在末尾。
  return numbers.map((number, index) => {
    const cost = getCost(number);
    return { number, index, cost: cost != null && Number.isFinite(cost) ? cost : Infinity };
  }).sort((a, b) =>
    (a.cost === b.cost ? 0 : a.cost < b.cost ? -1 : 1)
    || a.number.localeCompare(b.number)
    || a.index - b.index,
  ).map(({ index }) => index);
}
