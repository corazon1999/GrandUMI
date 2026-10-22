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
    <div data-don-deck={side} className={`${pileSizes[cardSize]} relative shrink-0`}>
      {/* 中、大牌手牌会向上覆盖卡堆底部，数量与名称放在上沿保持可见。 */}
      <div className="absolute inset-x-1 top-1 z-30 flex items-center gap-1">
        <span className="text-[10px] font-semibold text-slate-200 drop-shadow">咚卡组</span>
        <span data-don-deck-count className="rounded bg-slate-950/90 px-1 text-xs font-black text-white shadow">
          {count}
        </span>
      </div>
      {count > 0 ? (
        <DonCardItem state="deck" size={cardSize} disabled />
      ) : (
        <div className="h-full w-full rounded-md border-2 border-dashed border-slate-500/60 bg-slate-950/35" />
      )}
    </div>
  );
}
