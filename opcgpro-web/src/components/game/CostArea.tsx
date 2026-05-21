"use client";

import { useGameStore } from "@/store/gameStore";

interface Props {
  side: "my" | "opponent";
}

/** 简化版费用显示（仅对手方使用），己方用 DonArea */
export default function CostArea({ side }: Props) {
  const { active, rest, max } = useGameStore((s) => s[side].cost);

  return (
    <div className="flex items-center gap-1.5 text-xs">
      <div className="w-4 h-4 rounded-full bg-yellow-500 flex items-center justify-center">
        <span className="text-black text-[9px] font-bold leading-none">咚</span>
      </div>
      <span className="text-yellow-400 font-bold">
        {active}
        {rest > 0 && (
          <span className="text-gray-500">(+{rest})</span>
        )}
        <span className="text-gray-500">/{max}</span>
      </span>
    </div>
  );
}
