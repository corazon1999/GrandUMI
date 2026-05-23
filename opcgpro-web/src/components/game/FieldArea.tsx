"use client";

import { useGameStore } from "@/store/gameStore";
import { useBattleStore } from "@/store/battleStore";
import { useResponsive } from "@/hooks/useResponsive";
import CardItem from "@/components/ui/CardItem";
import DonCardItem from "@/components/game/DonCardItem";
import { getCard } from "@/data/CardLoader";

interface Props {
  side: "my" | "opponent";
}

export default function FieldArea({ side }: Props) {
  const player = useGameStore((s) => (side === "my" ? s.my : s.opponent));
  const isPending = useGameStore((s) => s.isPending);
  const selectedFieldId = useGameStore((s) => s.selectedFieldId);
  const selectedDonIndex = useGameStore((s) => s.selectedDonIndex);
  const setSelectedField = useGameStore((s) => s.setSelectedField);

  const isSelectingTarget = useBattleStore((s) => s.isSelectingTarget);
  const confirmAttackTarget = useBattleStore((s) => s.confirmAttackTarget);
  const { cardSize } = useResponsive();

  if (!player) return <div className="min-h-28" />;

  const characterCards = player.fieldCards;
  const stageCardNumber = player.stageNumber;

  const handleCardClick = (cardId: string) => {
    if (isPending) return;

    // 选目标攻击中：点击对方角色 = 确认攻击目标
    if (isSelectingTarget && side === "opponent") {
      confirmAttackTarget({ isLeader: false, cardId });
      return;
    }

    setSelectedField(selectedFieldId === cardId ? null : cardId);
  };

  return (
    <div className="flex items-center justify-center gap-2 px-4 py-2 min-h-28">
      {/* 舞台卡槽 */}
      <div className={`${cardSize === "sm" ? "w-14 h-20" : "w-20 h-28"} rounded-lg border border-dashed border-gray-700 flex items-center justify-center shrink-0`}>
        {stageCardNumber ? (
          <CardItem card={getCard(stageCardNumber) ?? null} size={cardSize} />
        ) : (
          <span className="text-gray-700 text-[10px]">舞台</span>
        )}
      </div>

      <div className="w-px h-20 bg-gray-700 shrink-0" />

      {/* 角色卡区域 */}
      <div className="flex items-end gap-2 overflow-x-auto flex-1">
        {characterCards.map((fc) => {
          const cardData = getCard(fc.number) ?? null;
          const attachedCount = fc.attachedDon;
          return (
            <div key={fc.id} className="relative flex flex-col items-center">
              {attachedCount > 0 && (
                <div className="relative mb-0.5">
                  {Array.from({ length: Math.min(attachedCount, 6) }).map((_, j) => (
                    <div
                      key={j}
                      className="relative"
                      style={{ zIndex: j, marginBottom: j < attachedCount - 1 ? "-20px" : "0" }}
                    >
                      <DonCardItem state="attached" size="sm" disabled />
                    </div>
                  ))}
                  {attachedCount > 6 && (
                    <span className="absolute -top-1 -right-2 text-yellow-500 text-[9px] font-bold">
                      +{attachedCount - 6}
                    </span>
                  )}
                </div>
              )}

              <div className="relative">
                <CardItem
                  card={cardData}
                  isSelected={
                    selectedFieldId === fc.id ||
                    (isSelectingTarget && side === "opponent" && !isPending)
                  }
                  isTapped={fc.isTapped}
                  powerBuff={fc.powerCurrent - (cardData?.power ?? 0) - attachedCount * 1000}
                  attachedDonCount={attachedCount}
                  size={cardSize}
                  onClick={() => handleCardClick(fc.id)}
                />
                {isSelectingTarget && side === "opponent" && !isPending && (
                  <div className="absolute -top-1 -right-1 w-4 h-4 bg-red-500 rounded-full animate-pulse shadow-lg shadow-red-500/50" />
                )}
                {selectedDonIndex !== null && side === "my" && !isPending && (
                  <div className="absolute -top-1 -left-1 w-4 h-4 bg-yellow-400 rounded-full animate-pulse shadow-lg shadow-yellow-400/50 flex items-center justify-center">
                    <span className="text-black text-[8px] font-bold">+</span>
                  </div>
                )}
              </div>
            </div>
          );
        })}
        {characterCards.length === 0 && (
          <span className="text-gray-700 text-xs">
            {side === "my" ? "我方场地" : "对手场地"}
          </span>
        )}
      </div>
    </div>
  );
}
