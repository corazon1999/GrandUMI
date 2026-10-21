"use client";

import { useEffect, useState } from "react";
import { useTournamentClock } from "@/net/TournamentClient";

export default function TournamentClockBanner() {
  const { clock, offset } = useTournamentClock();
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => { if (!clock) return; const timer = setInterval(() => setNow(Date.now()), 1000); return () => clearInterval(timer); }, [clock]);
  if (!clock) return null;
  const seconds = Math.max(0, Math.ceil((clock.deadline - now - offset) / 1000));
  const time = `${Math.floor(seconds / 60).toString().padStart(2, "0")}:${(seconds % 60).toString().padStart(2, "0")}`;
  return <div role="status" data-tournament-clock className="pointer-events-none flex shrink-0 flex-col gap-1 rounded-md border border-amber-700/60 bg-amber-950/90 p-2 text-xs text-amber-100">
    <span>{clock.stage === "swiss" ? "瑞士轮" : "淘汰赛"}第{clock.round}轮 · 第{clock.table}桌</span>
    <strong className="shrink-0 tabular-nums">{clock.manual ? "等待人工结算" : clock.extraTurnLimit !== null ? "追加回合中" : `本桌 ${time}`}</strong>
  </div>;
}
