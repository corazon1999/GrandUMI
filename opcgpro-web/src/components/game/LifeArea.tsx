"use client";

import { useGameStore } from "@/store/gameStore";

interface Props {
  side: "my" | "opponent";
}

export default function LifeArea({ side }: Props) {
  const count = useGameStore((s) => (side === "my" ? s.my?.lifeCount : s.opponent?.lifeCount) ?? 0);

  return (
    <div className="flex flex-col items-center gap-1">
      <span className="text-gray-400 text-[10px]">
        {side === "my" ? "生命" : "对手"}
      </span>
      <div className="flex flex-col gap-0.5">
        {Array.from({ length: Math.max(count, 0) }).map((_, i) => (
          <div
            key={i}
            className="w-8 h-2 rounded-sm bg-gradient-to-r from-red-500 to-orange-500"
          />
        ))}
        {count === 0 && (
          <div className="w-8 h-2 rounded-sm bg-gray-700" />
        )}
      </div>
      <span className="text-white text-xs font-bold">{count}</span>
    </div>
  );
}
