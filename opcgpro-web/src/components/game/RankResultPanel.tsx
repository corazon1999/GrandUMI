import type { RankPlayerSettlement } from "@/types/net";

interface RankResultPanelProps {
  result: RankPlayerSettlement;
}

const signedRp = (value: number) => `${value >= 0 ? "+" : ""}${value}`;

const rankDifferenceLabel = (result: RankPlayerSettlement) => {
  if (result.rankDifference < 0) {
    return `${result.won ? "低分方获胜奖励" : "低分方失败保护"}（低 ${Math.abs(result.rankDifference)} RP）`;
  }
  if (result.rankDifference > 0) {
    return `${result.won ? "高分方获胜削减" : "高分方失败追加扣分"}（高 ${result.rankDifference} RP）`;
  }
  return "赛前与对手同分";
};

export default function RankResultPanel({ result }: RankResultPanelProps) {
  return (
    <div className="mt-4 w-full max-w-sm rounded-xl border border-violet-400/40 bg-violet-950/80 px-4 py-3 text-center sm:px-5">
      <p className="text-xs font-bold text-violet-300">排位结算</p>
      <p className="mt-1 text-lg font-black text-white">
        {result.placementGames < result.placementRequired
          ? `定级进度 ${result.placementGames}/${result.placementRequired}`
          : `${result.tier}${result.division ? ` ${["", "I", "II", "III"][result.division]}` : ""}`}
      </p>
      {result.placementGames >= result.placementRequired && (
        <>
          <p className={`mt-1 text-2xl font-black ${result.rankPointDelta >= 0 ? "text-emerald-300" : "text-red-300"}`}>
            {signedRp(result.rankPointDelta)} RP
          </p>
          {result.rankPointFormulaApplied && (
            <dl data-testid="rank-rp-breakdown" className="mt-3 space-y-1.5 border-t border-white/10 pt-3 text-xs text-gray-200">
              <div className="flex items-center justify-between gap-4">
                <dt>基础{result.won ? "胜利" : "失败"}</dt>
                <dd className="font-bold">{signedRp(result.baseRankPointDelta)} RP</dd>
              </div>
              <div className="flex items-center justify-between gap-4">
                <dt>{result.resultStreak}连{result.won ? "胜奖励" : "败保护"}{result.resultStreak >= 6 ? "（已封顶）" : ""}</dt>
                <dd className="font-bold text-emerald-300">{signedRp(result.streakAdjustment)} RP</dd>
              </div>
              <div className="flex items-center justify-between gap-4 text-left">
                <dt>{rankDifferenceLabel(result)}</dt>
                <dd className={`shrink-0 font-bold ${result.rankDifferenceAdjustment >= 0 ? "text-emerald-300" : "text-red-300"}`}>
                  {signedRp(result.rankDifferenceAdjustment)} RP
                </dd>
              </div>
              {result.rankProtectionAdjustment > 0 && (
                <div className="flex items-center justify-between gap-4">
                  <dt>段位保护</dt>
                  <dd className="font-bold text-emerald-300">{signedRp(result.rankProtectionAdjustment)} RP</dd>
                </div>
              )}
              <div className="flex items-center justify-between gap-4 border-t border-white/10 pt-1.5 text-sm text-white">
                <dt className="font-bold">最终变化</dt>
                <dd className="font-black">{signedRp(result.rankPointDelta)} RP</dd>
              </div>
            </dl>
          )}
        </>
      )}
    </div>
  );
}
