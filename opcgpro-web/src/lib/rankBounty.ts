const BERRIES_PER_RANK_POINT = 100_000;
const BERRIES_PER_YI = 100_000_000;
const BERRIES_PER_WAN = 10_000;

/** 将内部排位分换算为玩家可见的悬赏金。1 分 = 10 万贝里。 */
export function formatRankBounty(rankPoints: number): string {
  const berries = Math.round(Math.abs(rankPoints) * BERRIES_PER_RANK_POINT);
  if (berries === 0) return "0贝里";

  const yi = Math.floor(berries / BERRIES_PER_YI);
  const wan = Math.floor((berries % BERRIES_PER_YI) / BERRIES_PER_WAN);
  const amount = `${yi > 0 ? `${yi}亿` : ""}${wan > 0 ? `${wan}万` : ""}`;
  return `${amount}贝里`;
}

/** 格式化悬赏金变化，正数显式带“+”。 */
export function formatSignedRankBounty(rankPointDelta: number): string {
  const sign = rankPointDelta > 0 ? "+" : rankPointDelta < 0 ? "-" : "";
  return `${sign}${formatRankBounty(rankPointDelta)}`;
}
