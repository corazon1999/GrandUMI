"use client";

import { useGameStore } from "@/store/gameStore";
import { useResponsive } from "@/hooks/useResponsive";
import CardItem from "@/components/ui/CardItem";
import { getGameCard } from "@/data/CardLoader";

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
  const stages = player?.stages ?? [];
  const dimensions = slotSizes[cardSize];

  // 仅己方舞台卡可被选中（用于发动【启动主要】）；海克斯“三号船坞”下两张舞台分别操作。
  const handleClick = (stageId: string) => {
    if (side !== "my" || isPending) return;
    setSelectedField(selectedFieldId === stageId ? null : stageId);
  };

  return (
    <div className="flex items-center justify-center gap-1.5" data-stage-slot-group data-zone-side={side}>
      {(stages.length > 0 ? stages : [null]).map((stage, index) => (
        <div
          key={stage?.id ?? `empty-stage-${side}`}
          className={`${dimensions} relative flex items-center justify-center rounded-md border border-dashed border-sky-200/25 bg-black/20 shadow-inner shadow-black/30`}
          data-zone="stage"
          data-stage-index={index}
          data-zone-side={side}
          data-zone-card-id={stage?.id}
          aria-label={stage ? `${side === "my" ? "我的" : "对手的"}舞台 ${index + 1}` : "舞台区"}
        >
          {stage ? (
            <CardItem
              card={getGameCard(stage.number, player?.spriteMap) ?? null}
              size={cardSize}
              isSelected={selectedFieldId === stage.id}
              isTapped={stage.tapped}
              oncePerTurnEffectAvailable={stage.oncePerTurnEffectAvailable}
              onClick={side === "my" && !isPending ? () => handleClick(stage.id) : undefined}
            />
          ) : (
            <span className="text-xs font-black text-slate-600">STAGE</span>
          )}
        </div>
      ))}
    </div>
  );
}
