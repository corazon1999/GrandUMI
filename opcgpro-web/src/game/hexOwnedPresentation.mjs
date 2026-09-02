/**
 * 保留玩家信息卡前三个固定槽位，并计算其余任意数量海克斯的统一入口。
 * 完整 items 不在这里截断，“查看全部”浮层始终使用权威快照数组。
 *
 * @template T
 * @param {readonly T[]} items
 * @param {number} [visibleLimit=3]
 */
export function buildOwnedHexPresentation(items, visibleLimit = 3) {
  const normalizedLimit = Math.max(0, Math.trunc(visibleLimit));
  return {
    visibleItems: items.slice(0, normalizedLimit),
    overflowCount: Math.max(0, items.length - normalizedLimit),
  };
}
