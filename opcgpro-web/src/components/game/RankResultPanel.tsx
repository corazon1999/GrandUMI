import type { RankPlayerSettlement } from "@/types/net";
import { formatRankBounty, formatSignedRankBounty } from "@/lib/rankBounty";
import { SeaNameBadge } from "@/components/ui/HunterIdentity";
import HunterDefeatCount from "@/components/ui/HunterDefeatCount";

interface RankResultPanelProps {
  result: RankPlayerSettlement;
}

const rankDifferenceLabel = (result: RankPlayerSettlement) => {
  if (result.rankDifference < 0) {
    return `${result.won ? "低悬赏方获胜奖励" : "低悬赏方失败保护"}（低 ${formatRankBounty(result.rankDifference)}）`;
  }
  if (result.rankDifference > 0) {
    return `${result.won ? "高悬赏方获胜削减" : "高悬赏方失败追加扣除"}（高 ${formatRankBounty(result.rankDifference)}）`;
  }
  return "赛前与对手悬赏金相同";
};

export default function RankResultPanel({ result }: RankResultPanelProps) {
  if (result.isHunterSeason) {
    return <section data-hunter-result className="mt-3 w-full max-w-sm rounded-2xl border border-cyan-300/30 bg-[radial-gradient(ellipse_at_top,rgba(6,182,212,.2),transparent_70%),linear-gradient(145deg,#0b1628,#050811)] px-4 py-3 text-center shadow-[0_0_35px_rgba(6,182,212,.1)]">
      <div className="flex items-center justify-between gap-2 text-xs"><SeaNameBadge sea={result.faction} /><span className="font-bold text-cyan-200">S2 · 猎人战报</span></div>
      <div className="mt-2 flex items-center justify-center gap-3"><strong className="text-3xl font-black tabular-nums text-cyan-200"><HunterDefeatCount value={result.rankPointDelta} signed/></strong><b className="text-xs text-white">{result.tier}</b></div>
      <p className="mt-1 text-xs text-slate-400">累计 <HunterDefeatCount value={result.rankPointsAfter} className="font-bold text-white"/></p>
      {result.won && result.rankPointFormulaApplied ? <dl className="mt-2 grid gap-1 border-t border-white/10 pt-2 text-xs text-slate-300">
        <div className="flex justify-between"><dt>击败对手</dt><dd><HunterDefeatCount value={1} signed/></dd></div>
        {result.streakAdjustment > 0 && <div className="flex justify-between"><dt>{result.resultStreak} 连胜追加</dt><dd className="text-cyan-200"><HunterDefeatCount value={result.streakAdjustment} signed/></dd></div>}
        {result.winStreakEndedBounty > 0 && <div className="flex justify-between"><dt>终结对手 {result.endedWinStreak} 连胜</dt><dd className="text-amber-200"><HunterDefeatCount value={2} signed/></dd></div>}
        {result.rankDifferenceAdjustment > 0 && <div className="flex justify-between"><dt>击败领先海域猎人</dt><dd className="text-emerald-200"><HunterDefeatCount value={1} signed/></dd></div>}
      </dl> : <p className="mt-2 border-t border-white/10 pt-2 text-[11px] text-slate-400">{!result.rankPointFormulaApplied ? "赛季已截止，击败数量停止结算" : "失败不减少击败数量，连胜已中断"}</p>}
    </section>;
  }
  const baseDelta = Math.abs(result.baseRankPointDelta);
  // 20 亿档严格取 10 亿档的两倍，因此连败保护上限是 63 × 2 = 126。
  const lossStreakCap = baseDelta === 500 ? 126 : Math.ceil(baseDelta / 4);
  const streakCap = result.won ? baseDelta / 2 : lossStreakCap;
  const streakCapped = result.streakAdjustment >= streakCap;

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
            悬赏金{formatSignedRankBounty(result.rankPointDelta)}
          </p>
          {result.rankPointFormulaApplied && (
            <dl data-testid="rank-rp-breakdown" className="mt-3 space-y-1.5 border-t border-white/10 pt-3 text-xs text-gray-200">
              <div className="flex items-center justify-between gap-4">
                <dt>基础{result.won ? "胜利" : "失败"}</dt>
                <dd className="font-bold">{formatSignedRankBounty(result.baseRankPointDelta)}</dd>
              </div>
              <div className="flex items-center justify-between gap-4">
                <dt>{result.resultStreak}连{result.won ? "胜奖励" : "败保护"}{streakCapped ? "（已封顶）" : ""}</dt>
                <dd className="font-bold text-emerald-300">{formatSignedRankBounty(result.streakAdjustment)}</dd>
              </div>
              {result.winStreakEndedBounty > 0 && (
                <div className="flex items-center justify-between gap-4">
                  <dt>终结{result.endedWinStreak}连胜赏金</dt>
                  <dd className="font-bold text-amber-300">{formatSignedRankBounty(result.winStreakEndedBounty)}</dd>
                </div>
              )}
              <div className="flex items-center justify-between gap-4 text-left">
                <dt>{rankDifferenceLabel(result)}</dt>
                <dd className={`shrink-0 font-bold ${result.rankDifferenceAdjustment >= 0 ? "text-emerald-300" : "text-red-300"}`}>
                  {formatSignedRankBounty(result.rankDifferenceAdjustment)}
                </dd>
              </div>
              {result.rankProtectionAdjustment > 0 && (
                <div className="flex items-center justify-between gap-4">
                  <dt>段位保护</dt>
                  <dd className="font-bold text-emerald-300">{formatSignedRankBounty(result.rankProtectionAdjustment)}</dd>
                </div>
              )}
              <div className="flex items-center justify-between gap-4 border-t border-white/10 pt-1.5 text-sm text-white">
                <dt className="font-bold">最终变化</dt>
                <dd className="font-black">{formatSignedRankBounty(result.rankPointDelta)}</dd>
              </div>
            </dl>
          )}
        </>
      )}
    </div>
  );
}
