"use client";

import { useGameStore } from "@/store/gameStore";
import { useResponsive } from "@/hooks/useResponsive";
import DonCardItem from "./DonCardItem";

interface Props {
  side: "my" | "opponent";
}

const pileSizes = {
  sm: "h-[6.3rem] w-[4.5rem]",
  md: "h-[8.4rem] w-[6rem]",
  lg: "h-[11.2rem] w-[8rem]",
};

export default function DonDeckPile({ side }: Props) {
  const count = useGameStore((s) => (side === "my" ? s.my?.donDeckCount : s.opponent?.donDeckCount) ?? 0);
  const { cardSize } = useResponsive();

  return (
    <div className={`${pileSizes[cardSize]} relative shrink-0`}>
      <span className="absolute left-2 top-2 z-20 text-[11px] font-semibold text-slate-200 drop-shadow">
        DON 卡堆
      </span>
      {count > 0 ? (
        <DonCardItem state="deck" size={cardSize} disabled />
      ) : (
        <div className="h-full w-full rounded-md border-2 border-dashed border-slate-500/60 bg-slate-950/35" />
      )}
      <div className="absolute inset-x-0 bottom-1 z-30 flex justify-center">
        <span className="rounded bg-slate-950/90 px-2 py-0.5 text-xs font-black text-white shadow">
          {count}
        </span>
      </div>
    </div>
  );
}
