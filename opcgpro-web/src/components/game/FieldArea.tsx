"use client";

import { useGameStore } from "@/store/gameStore";
import { useBattleStore } from "@/store/battleStore";
import { useResponsive } from "@/hooks/useResponsive";
import CardItem from "@/components/ui/CardItem";
import DonCardItem from "@/components/game/DonCardItem";

interface Props {
  side: "my" | "opponent";
}

export default function FieldArea({ side }: Props) {
  const characterCards = useGameStore((s) => s[side].field.characterCards);
  const stageCard = useGameStore((s) => s[side].field.stageCard);
  const donCards = useGameStore((s) => s[side].cost.donCards);
  const isPending = useGameStore((s) => s.isPending);
  const selectedFieldIndex = useGameStore((s) => s.selectedFieldIndex);
  const selectedDonId = useGameStore((s) => s.selectedDonId);
  const setSelectedField = useGameStore((s) => s.setSelectedField);

  const isSelectingTarget = useBattleStore((s) => s.isSelectingTarget);
  const confirmAttackTarget = useBattleStore((s) => s.confirmAttackTarget);
  const { cardSize } = useResponsive();

  /** 计算某个角色上附着的咚卡数量 */
  const getAttachedCount = (fieldIndex: number): number =>
    donCards.filter(
      (d) => d.state === "attached" && d.attachedTo === fieldIndex,
    ).length;

  const handleCardClick = (index: number) => {
    if (isPending) return;

    // 如果正在选择攻击目标，点击对方角色卡即为确认攻击目标
    if (isSelectingTarget && side === "opponent") {
      confirmAttackTarget(index);
      return;
    }

    // 否则为普通选中/取消（若已选中咚卡则自动触发附着）
    setSelectedField(selectedFieldIndex === index ? null : index);
  };

  return (
    <div className="flex items-center justify-center gap-2 px-4 py-2 min-h-28">
      {/* 舞台卡槽 */}
      <div className={`${cardSize === "sm" ? "w-14 h-20" : "w-20 h-28"} rounded-lg border border-dashed border-gray-700 flex items-center justify-center shrink-0`}>
        {stageCard ? (
          <CardItem card={stageCard} size={cardSize} />
        ) : (
          <span className="text-gray-700 text-[10px]">舞台</span>
        )}
      </div>

      {/* 分隔线 */}
      <div className="w-px h-20 bg-gray-700 shrink-0" />

      {/* 角色卡区域 */}
      <div className="flex items-end gap-2 overflow-x-auto flex-1">
        {characterCards.map((fc, i) => {
          const attachedCount = getAttachedCount(i);
          return (
            <div key={`${fc.card.number}-${i}`} className="relative flex flex-col items-center">
              {/* 附着咚堆叠在角色下方 */}
              {attachedCount > 0 && (
                <div className="relative mb-0.5">
                  {Array.from({ length: Math.min(attachedCount, 6) }).map(
                    (_, j) => (
                      <div
                        key={j}
                        className="relative"
                        style={{
                          zIndex: j,
                          marginBottom: j < attachedCount - 1 ? "-20px" : "0",
                        }}
                      >
                        <DonCardItem state="attached" size="sm" disabled />
                      </div>
                    ),
                  )}
                  {attachedCount > 6 && (
                    <span className="absolute -top-1 -right-2 text-yellow-500 text-[9px] font-bold">
                      +{attachedCount - 6}
                    </span>
                  )}
                </div>
              )}

              {/* 角色卡本体 */}
              <div className="relative">
                <CardItem
                  card={fc.card}
                  isSelected={
                    fc.isSelected ||
                    (isSelectingTarget && side === "opponent" && !isPending) ||
                    selectedFieldIndex === i
                  }
                  isTapped={fc.isTapped}
                  powerBuff={fc.powerBuff}
                  attachedDonCount={attachedCount}
                  size={cardSize}
                  onClick={() => handleCardClick(i)}
                />
                {/* 攻击目标高亮指示器 */}
                {isSelectingTarget && side === "opponent" && !isPending && (
                  <div className="absolute -top-1 -right-1 w-4 h-4 bg-red-500 rounded-full animate-pulse shadow-lg shadow-red-500/50" />
                )}
                {/* 咚附着中指示器 */}
                {selectedDonId && side === "my" && !isPending && (
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
