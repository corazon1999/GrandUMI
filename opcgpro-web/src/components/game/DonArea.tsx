"use client";

import { useGameStore } from "@/store/gameStore";
import DonCardItem from "./DonCardItem";

interface Props {
  side: "my" | "opponent";
}

export default function DonArea({ side }: Props) {
  const player = useGameStore((s) => (side === "my" ? s.my : s.opponent));
  const currentTurn = useGameStore((s) => s.currentTurn);
  const isPending = useGameStore((s) => s.isPending);
  const selectedDonIndex = useGameStore((s) => s.selectedDonIndex);
  const setSelectedDon = useGameStore((s) => s.setSelectedDon);

  if (!player) return null;

  const isMy = side === "my";
  const canInteract = isMy && currentTurn && !isPending;
  const deckCount     = player.donDeckCount;
  const activeCount   = player.costActive;
  const restCount     = player.costRest;
  const attachedCount = player.costAttached;

  return (
    <div className="flex items-center gap-3 px-2 py-1">
      {/* 咚!!卡组 */}
      <div className="relative">
        <div className="flex -space-x-3">
          {deckCount > 0 ? (
            Array.from({ length: Math.min(deckCount, 5) }).map((_, i) => (
              <div key={i} className="relative" style={{ zIndex: deckCount - i }}>
                <DonCardItem state="deck" size="sm" disabled />
              </div>
            ))
          ) : (
            <span className="text-gray-700 text-[10px]">空</span>
          )}
        </div>
        <span className="absolute -bottom-3 left-1/2 -translate-x-1/2 text-gray-600 text-[9px]">
          {deckCount}
        </span>
      </div>

      <div className="w-px h-8 bg-gray-700" />

      {/* 活跃咚 */}
      <div className="relative">
        <div className="flex flex-wrap gap-0.5">
          {Array.from({ length: activeCount }).map((_, i) => (
            <DonCardItem
              key={`a${i}`}
              state="active"
              size="sm"
              isSelected={selectedDonIndex === i}
              onClick={canInteract ? () => setSelectedDon(selectedDonIndex === i ? null : i) : undefined}
              disabled={!canInteract}
            />
          ))}
        </div>
        <span className="absolute -bottom-3 left-1/2 -translate-x-1/2 text-yellow-500 text-[9px] font-bold">
          {activeCount}
        </span>
      </div>

      <div className="w-px h-8 bg-gray-700" />

      {/* 休息咚 */}
      <div className="relative">
        <div className="flex flex-wrap gap-0.5">
          {Array.from({ length: restCount }).map((_, i) => (
            <DonCardItem key={`r${i}`} state="rest" size="sm" disabled />
          ))}
        </div>
        <span className="absolute -bottom-3 left-1/2 -translate-x-1/2 text-gray-500 text-[9px]">
          {restCount}
        </span>
      </div>

      <div className="w-px h-8 bg-gray-700" />

      {/* 附着中咚（统计数字） */}
      <div className="text-gray-400 text-[10px]">附×{attachedCount}</div>
    </div>
  );
}
