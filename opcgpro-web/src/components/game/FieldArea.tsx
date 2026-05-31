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

  if (!player) return <div className="h-full min-h-0" />;

  const handleCardClick = (cardId: string) => {
    if (isPending) return;

    if (isSelectingTarget && side === "opponent") {
      confirmAttackTarget({ isLeader: false, cardId });
      return;
    }

    setSelectedField(selectedFieldId === cardId ? null : cardId);
  };

  return (
    <div className="flex h-full min-h-0 min-w-0 items-center justify-center gap-3 overflow-x-auto overflow-y-hidden rounded-md border border-sky-200/15 bg-black/15 px-4 py-1 shadow-inner shadow-black/30">
      {player.fieldCards.map((fc) => {
        const cardData = getCard(fc.number) ?? null;
        const attachedCount = fc.attachedDon;

        return (
          <div key={fc.id} className="relative flex h-full min-h-0 shrink-0 items-center">
            {attachedCount > 0 && (
              <div className="absolute -left-4 top-1 z-10">
                <DonCardItem state="attached" size={cardSize === "lg" ? "md" : "sm"} disabled />
                {attachedCount > 1 && (
                  <span className="absolute -right-2 -top-2 rounded bg-slate-950 px-1 text-[10px] font-black text-yellow-300">
                    {attachedCount}
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
                <div className="absolute -right-2 -top-2 h-5 w-5 animate-pulse rounded-full bg-red-500 shadow-lg shadow-red-500/50" />
              )}
              {selectedDonIndex !== null && side === "my" && !isPending && (
                <div className="absolute -left-2 -top-2 flex h-6 w-6 animate-pulse items-center justify-center rounded-full bg-yellow-300 shadow-lg shadow-yellow-300/50">
                  <span className="text-[10px] font-black text-black">+</span>
                </div>
              )}
            </div>
          </div>
        );
      })}

      {player.fieldCards.length === 0 && (
        <span className="text-xs font-semibold text-slate-600">
          {side === "my" ? "角色区" : "对手角色区"}
        </span>
      )}
    </div>
  );
}
