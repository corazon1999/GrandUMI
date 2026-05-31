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

function EmptyLeader({ label }: { label: string }) {
  return (
    <div className={`${label} relative flex items-center justify-center rounded-md border border-dashed border-sky-200/25 bg-black/20 shadow-inner shadow-black/30`}>
      <span className="absolute left-2 top-2 z-10 text-[11px] font-semibold text-slate-200 drop-shadow">
        领袖
      </span>
      <span className="text-xs font-black text-slate-600">LEADER</span>
    </div>
  );
}

export default function LeaderCard({ side }: Props) {
  const player = useGameStore((s) => (side === "my" ? s.my : s.opponent));
  const { cardSize } = useResponsive();
  const dimensions = slotSizes[cardSize];

  if (!player) {
    return <EmptyLeader label={dimensions} />;
  }

  const leader = getCard(player.leaderNumber);
  if (!leader) {
    return (
      <div className={`${dimensions} relative flex items-center justify-center rounded-md border border-dashed border-sky-200/25 bg-black/20 shadow-inner shadow-black/30`}>
        <span className="absolute left-2 top-2 z-10 text-[11px] font-semibold text-slate-200 drop-shadow">
          领袖
        </span>
        <span className="text-[10px] text-gray-500">{player.leaderNumber}</span>
      </div>
    );
  }

  return (
    <div className={`${dimensions} relative`}>
      <CardItem
        card={leader}
        size={cardSize}
        isTapped={player.leaderTapped}
        attachedDonCount={player.leaderAttachedDon}
        powerBuff={player.leaderPower - (leader.power ?? 0) - player.leaderAttachedDon * 1000}
      />
      <span className="pointer-events-none absolute left-2 top-2 z-10 text-[11px] font-semibold text-slate-100 drop-shadow">
        领袖
      </span>
    </div>
  );
}
