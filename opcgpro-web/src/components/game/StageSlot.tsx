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
  const isPending = useGameStore((s) => s.isPending);
  const selectedFieldId = useGameStore((s) => s.selectedFieldId);
  const setSelectedField = useGameStore((s) => s.setSelectedField);
  const { cardSize } = useResponsive();
  const stageNumber = player?.stageNumber ?? null;
  const stageId = player?.stageId ?? null;
  const stageTapped = player?.stageTapped ?? false;
  const dimensions = slotSizes[cardSize];

  // 仅己方舞台卡可被选中（用于发动【启动主要】）
  const clickable = side === "my" && !!stageId && !isPending;
  const handleClick = () => {
    if (!clickable || !stageId) return;
    setSelectedField(selectedFieldId === stageId ? null : stageId);
  };

  return (
    <div className={`${dimensions} relative flex items-center justify-center rounded-md border border-dashed border-sky-200/25 bg-black/20 shadow-inner shadow-black/30`}>
      {stageNumber ? (
        <CardItem
          card={getCard(stageNumber) ?? null}
          size={cardSize}
          isSelected={selectedFieldId === stageId}
          isTapped={stageTapped}
          onClick={clickable ? handleClick : undefined}
        />
      ) : (
        <span className="text-xs font-black text-slate-600">STAGE</span>
      )}
    </div>
  );
}
