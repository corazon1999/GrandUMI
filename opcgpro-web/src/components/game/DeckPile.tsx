"use client";

import { useGameStore } from "@/store/gameStore";
import { useResponsive } from "@/hooks/useResponsive";
import CardBack from "@/components/ui/CardBack";

interface Props {
  side: "my" | "opponent";
}

const pileSizes = {
  sm: "h-[6.3rem] w-[4.5rem]",
  md: "h-[8.4rem] w-[6rem]",
  lg: "h-[11.2rem] w-[8rem]",
};

export default function DeckPile({ side }: Props) {
  const player = useGameStore((s) => (side === "my" ? s.my : s.opponent));
  const count = player?.deckCount ?? 0;
  const { cardSize } = useResponsive();

  return (
    <div className="flex flex-col items-center gap-2 rounded-md border border-sky-200/15 bg-black/30 px-2.5 py-2 shadow-lg shadow-black/25">
      <span className="text-[11px] font-semibold text-slate-300">牌库</span>
      <div
        className={`relative ${pileSizes[cardSize]}`}
        data-zone="deck"
        data-zone-side={side}
      >
        <div className="absolute inset-0 translate-x-2 translate-y-2 rounded-md border border-sky-300/20 bg-slate-950" />
        <div className="absolute inset-0 translate-x-1 translate-y-1 rounded-md border border-sky-300/30 bg-blue-950" />
        <div className="absolute inset-0 overflow-hidden rounded-md shadow-xl shadow-black/40">
          <CardBack cardBackId={player?.cardBackId} side={side} />
        </div>
        <div className="absolute -right-3 -top-3 flex h-8 min-w-8 items-center justify-center rounded-md border border-white/20 bg-slate-950 px-1 text-base font-black text-white shadow">
          {count}
        </div>
      </div>
    </div>
  );
}
