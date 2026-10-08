"use client";
import { useEffect, useState } from "react";
import type { RankProfileSnapshot, RankFaction, RankedMode } from "@/types/net";
import { HomeRequest } from "@/net/HomeProtocol";
import { HUNTER_SEAS, SeaChoiceCard, SeaNameBadge, EquippedSeasonTitleBadge } from "@/components/ui/HunterIdentity";
import { formatHunterHeads } from "@/lib/rankAffiliation";
import { showMessage } from "@/components/ui/MessageBox";

export default function HunterSeasonPanel({ profile, mode }: { profile: RankProfileSnapshot; mode: RankedMode }) {
  const [pendingSea, setPendingSea] = useState<RankFaction | null>(null);
  useEffect(() => setPendingSea(null), [mode, profile.faction]);
  useEffect(() => {
    if (!pendingSea) return;
    const timer = setTimeout(() => setPendingSea(null), 10_000);
    return () => clearTimeout(timer);
  }, [pendingSea]);
  const selectSea = (sea: RankFaction) => {
    if (pendingSea) return;
    if (HomeRequest.selectRankFaction(sea, false, mode)) setPendingSea(sea);
    else showMessage("服务器未连接，请重连后选择出海海域", "error");
  };
  const next = [10,100,1000,10000].find(n => n > profile.rankPoints);
  return <section data-hunter-season-panel className="rounded-2xl border border-cyan-500/25 bg-[radial-gradient(ellipse_at_top,rgba(8,145,178,.12),transparent_65%),linear-gradient(145deg,#0b1120,#050811)] p-3 sm:p-5">
    <header className="flex flex-wrap items-center justify-between gap-2"><div>
      <p className="text-[10px] font-bold tracking-[.25em] text-cyan-300/70">S2 · 四海启航</p>
      <h3 className="mt-1 text-xl font-black text-white">赏金猎人排位赛</h3>
    </div><span className="rounded-full border border-amber-400/25 px-3 py-1 text-[10px] text-amber-200">11 月 30 日结算</span></header>
    <EquippedSeasonTitleBadge identity={profile}/>
    {!profile.faction ? <><p className="mt-4 text-xs leading-5 text-slate-400">选择你的出海海域。所有猎人从 0 人头出发，航线在本赛季锁定。</p>
      <div className="mt-3 grid grid-cols-2 gap-2 sm:grid-cols-4">{HUNTER_SEAS.map(sea => <SeaChoiceCard key={sea.id} sea={sea}
        disabled={pendingSea !== null} selected={pendingSea === sea.id} pending={pendingSea === sea.id}
        onSelect={() => selectSea(sea.id)}/>)}</div></>
      : <div className="mt-4 flex flex-wrap items-center gap-4 rounded-xl border border-white/5 bg-black/25 p-3">
        <SeaNameBadge sea={profile.faction as RankFaction}/><div className="min-w-0 flex-1"><p className="text-sm font-bold text-white">{profile.tier}</p>
          <p className="mt-1 text-xl font-black tabular-nums text-cyan-200">{formatHunterHeads(profile.rankPoints)}</p>
          <p className="mt-1 text-[10px] text-slate-500">{next ? `距离下一段位还需 ${(next-profile.rankPoints).toLocaleString()} 人头` : "猎人巅峰 · 继续争夺四海榜首"}</p></div>
        <span className="text-xs text-slate-400">{profile.wins} 胜 · {profile.losses} 负</span></div>}
    <details className="mt-3 rounded-xl border border-white/5 bg-black/15 text-xs text-slate-400"><summary className="min-h-11 cursor-pointer px-3 py-3 font-bold text-cyan-200">猎人规则与段位</summary>
      <ul className="space-y-2 px-4 pb-4 leading-5"><li>胜利获得 1 人头；第 3 连胜起，本局奖励为连胜场次减 1。</li><li>终结对手至少 3 连胜，额外 +2 人头。</li><li>击败结算前累计人头最多海域的玩家，额外 +1；非零并列第一均有效。</li><li>失败不扣人头并中断连胜，平局不结算且保留连胜。奖励可以叠加。</li><li>10 / 100 / 1000 / 10000 人头对应十人斩、百人斩、千人斩、万人斩。</li><li>无启动倍率、无定级门槛；S1 背包余额及已购装饰原样保留。</li></ul>
    </details>
  </section>;
}
