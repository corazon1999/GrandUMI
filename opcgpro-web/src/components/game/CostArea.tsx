"use client";

import { useGameStore } from "@/store/gameStore";

interface Props {
  side: "my" | "opponent";
}

export default function CostArea({ side }: Props) {
  const player = useGameStore((s) => (side === "my" ? s.my : s.opponent));
  const active = player?.costActive ?? 0;
  const rest = player?.costRest ?? 0;
  const attached = player?.costAttached ?? 0;
  const total = active + rest + attached;

  return (
    <div className="flex items-center gap-2 rounded-md border border-yellow-300/30 bg-yellow-300/10 px-2 py-1 shadow-sm">
      <div className="flex h-7 w-7 items-center justify-center rounded-full bg-yellow-300 text-[9px] font-black leading-none text-black shadow">
        DON
      </div>
      <div className="flex flex-col leading-none">
        <div className="flex items-baseline gap-1">
          <span className="text-base font-black text-yellow-200">{active}</span>
          <span className="text-[10px] font-semibold text-gray-400">/ {total}</span>
        </div>
        <span className="text-[9px] text-gray-400">
          活跃 {active} · 休息 {rest} · 附着 {attached}
        </span>
      </div>
    </div>
  );
}
