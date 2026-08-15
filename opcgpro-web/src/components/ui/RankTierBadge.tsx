import type { RankFaction } from "@/types/net";

const SUPREME_RANK_TITLES = new Set(["海贼王", "海军元帅", "世界之王"]);
const ELITE_RANK_TITLES = new Set(["四皇", "海军大将", "神之骑士团"]);

export function rankTierLabel(tier: string, division?: number | null): string {
  return `${tier}${division ? ` ${["", "I", "II", "III"][division]}` : ""}`;
}

export default function RankTierBadge({
  faction,
  tier,
  division,
  className = "",
}: {
  faction: RankFaction;
  tier: string;
  division?: number | null;
  className?: string;
}) {
  const label = rankTierLabel(tier, division);
  const effect = SUPREME_RANK_TITLES.has(tier)
    ? "supreme"
    : ELITE_RANK_TITLES.has(tier)
      ? "elite"
      : null;

  if (!effect) return <span className={className}>{label}</span>;

  return (
    <span
      className={`rank-tier-badge rank-tier-badge--${effect} rank-tier-badge--${faction} ${className}`}
      data-rank-effect={effect}
    >
      <span className="rank-tier-badge__aura" aria-hidden="true" />
      <span className="rank-tier-badge__emblem" aria-hidden="true">{effect === "supreme" ? "♛" : "✦"}</span>
      <span className="rank-tier-badge__label">{label}</span>
    </span>
  );
}
