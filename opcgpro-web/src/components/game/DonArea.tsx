"use client";

import { useMemo } from "react";
import { useGameStore } from "@/store/gameStore";
import DonCardItem from "./DonCardItem";
import type { DonCard, DonState } from "@/types/game";

interface Props {
  side: "my" | "opponent";
}

export default function DonArea({ side }: Props) {
  const donCards = useGameStore((s) => s[side].cost.donCards);
  const currentTurn = useGameStore((s) => s.currentTurn);
  const isPending = useGameStore((s) => s.isPending);
  const selectedDonId = useGameStore((s) => s.selectedDonId);
  const setSelectedDon = useGameStore((s) => s.setSelectedDon);

  const isMy = side === "my";
  const canInteract = isMy && currentTurn && !isPending;

  // 按状态分组咚卡
  const groups = useMemo(() => {
    const result: Record<DonState, DonCard[]> = {
      deck: [],
      active: [],
      rest: [],
      attached: [],
    };
    for (const don of donCards) {
      result[don.state].push(don);
    }
    return result;
  }, [donCards]);

  const handleDonClick = (don: DonCard) => {
    if (!canInteract || don.state !== "active") return;
    setSelectedDon(don.id);
  };

  return (
    <div className="flex items-center gap-3 px-2 py-1">
      {/* 咚!!卡组（牌背堆叠） */}
      <div className="relative">
        <div className="flex -space-x-3">
          {groups.deck.length > 0 ? (
            groups.deck.map((don, i) => (
              <div
                key={don.id}
                className="relative"
                style={{ zIndex: groups.deck.length - i }}
              >
                <DonCardItem state="deck" size="sm" disabled />
              </div>
            ))
          ) : (
            <span className="text-gray-700 text-[10px]">空</span>
          )}
        </div>
        <span className="absolute -bottom-3 left-1/2 -translate-x-1/2 text-gray-600 text-[9px]">
          {groups.deck.length}
        </span>
      </div>

      <div className="w-px h-8 bg-gray-700" />

      {/* 活跃咚（可点击附着） */}
      <div className="relative">
        <div className="flex flex-wrap gap-0.5">
          {groups.active.map((don) => (
            <DonCardItem
              key={don.id}
              state="active"
              size="sm"
              isSelected={selectedDonId === don.id}
              onClick={() => handleDonClick(don)}
              disabled={!canInteract}
            />
          ))}
        </div>
        <span className="absolute -bottom-3 left-1/2 -translate-x-1/2 text-yellow-500 text-[9px] font-bold">
          {groups.active.length}
        </span>
      </div>

      <div className="w-px h-8 bg-gray-700" />

      {/* 休息咚（横置显示） */}
      <div className="relative">
        <div className="flex flex-wrap gap-0.5">
          {groups.rest.map((don) => (
            <DonCardItem key={don.id} state="rest" size="sm" disabled />
          ))}
        </div>
        <span className="absolute -bottom-3 left-1/2 -translate-x-1/2 text-gray-500 text-[9px]">
          {groups.rest.length}
        </span>
      </div>
    </div>
  );
}
