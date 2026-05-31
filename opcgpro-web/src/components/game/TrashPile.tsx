"use client";

import { useGameStore } from "@/store/gameStore";
import { useResponsive } from "@/hooks/useResponsive";

interface Props {
  side: "my" | "opponent";
}

const pileSizes = {
  sm: "h-[6.3rem] w-[4.5rem]",
  md: "h-[8.4rem] w-[6rem]",
  lg: "h-[11.2rem] w-[8rem]",
};

export default function TrashPile({ side }: Props) {
  const count = useGameStore((s) => (side === "my" ? s.my?.trashNumbers.length : s.opponent?.trashNumbers.length) ?? 0);
  const { cardSize } = useResponsive();

  return (
    <div className="flex flex-col items-center gap-2 rounded-md border border-zinc-300/15 bg-black/30 px-2.5 py-2 shadow-lg shadow-black/25">
      <span className="text-[11px] font-semibold text-slate-300">墓地</span>
      <div className={`relative flex items-center justify-center rounded-md border-2 border-dashed border-zinc-400/35 bg-zinc-950/60 ${pileSizes[cardSize]}`}>
        <span className="text-xs font-black text-zinc-500">TRASH</span>
        <div className="absolute -right-3 -top-3 flex h-8 min-w-8 items-center justify-center rounded-md border border-white/20 bg-slate-950 px-1 text-base font-black text-white shadow">
          {count}
        </div>
      </div>
    </div>
  );
}
