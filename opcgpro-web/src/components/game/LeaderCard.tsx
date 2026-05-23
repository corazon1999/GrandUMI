"use client";

import { useGameStore } from "@/store/gameStore";
import { useResponsive } from "@/hooks/useResponsive";
import CardItem from "@/components/ui/CardItem";
import { getCard } from "@/data/CardLoader";

interface Props {
  side: "my" | "opponent";
}

export default function LeaderCard({ side }: Props) {
  const player = useGameStore((s) => (side === "my" ? s.my : s.opponent));
  const { cardSize } = useResponsive();
  const dimensions = cardSize === "sm" ? "w-14 h-20" : "w-20 h-28";

  if (!player) {
    return (
      <div className={`${dimensions} rounded-lg border border-dashed border-gray-700 flex items-center justify-center`}>
        <span className="text-gray-700 text-[10px]">领航</span>
      </div>
    );
  }

  const leader = getCard(player.leaderNumber);
  if (!leader) {
    return (
      <div className={`${dimensions} rounded-lg border border-dashed border-gray-700 flex items-center justify-center`}>
        <span className="text-gray-700 text-[10px]">{player.leaderNumber}</span>
      </div>
    );
  }

  return (
    <CardItem
      card={leader}
      size={cardSize}
      isTapped={player.leaderTapped}
      attachedDonCount={player.leaderAttachedDon}
      powerBuff={player.leaderPower - (leader.power ?? 0) - player.leaderAttachedDon * 1000}
    />
  );
}
