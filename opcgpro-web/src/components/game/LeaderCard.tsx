"use client";

import { useGameStore } from "@/store/gameStore";
import { useResponsive } from "@/hooks/useResponsive";
import CardItem from "@/components/ui/CardItem";

interface Props {
  side: "my" | "opponent";
}

export default function LeaderCard({ side }: Props) {
  const leader = useGameStore((s) => s[side].leader);
  const { cardSize } = useResponsive();
  const dimensions = cardSize === "sm" ? "w-14 h-20" : "w-20 h-28";

  if (!leader) {
    return (
      <div className={`${dimensions} rounded-lg border border-dashed border-gray-700 flex items-center justify-center`}>
        <span className="text-gray-700 text-[10px]">领航</span>
      </div>
    );
  }

  return <CardItem card={leader} size={cardSize} />;
}
