"use client";

import { useGameStore } from "@/store/gameStore";
import { useResponsive } from "@/hooks/useResponsive";
import CardItem from "@/components/ui/CardItem";
import { getCard } from "@/data/CardLoader";

interface Props {
  side: "my" | "opponent";
}

const slotSizes = {
  sm: "w-[4.5rem] h-[6.3rem]",
  md: "w-[6rem] h-[8.4rem]",
  lg: "w-[8rem] h-[11.2rem]",
};

export default function StageSlot({ side }: Props) {
  const player = useGameStore((s) => (side === "my" ? s.my : s.opponent));
  const { cardSize } = useResponsive();
  const stageNumber = player?.stageNumber ?? null;
  const dimensions = slotSizes[cardSize];

  return (
    <div className={`${dimensions} relative flex items-center justify-center rounded-md border border-dashed border-sky-200/25 bg-black/20 shadow-inner shadow-black/30`}>
      <span className="absolute left-2 top-2 z-10 text-[11px] font-semibold text-slate-200 drop-shadow">
        场地
      </span>
      {stageNumber ? (
        <CardItem card={getCard(stageNumber) ?? null} size={cardSize} />
      ) : (
        <span className="text-xs font-black text-slate-600">STAGE</span>
      )}
    </div>
  );
}
