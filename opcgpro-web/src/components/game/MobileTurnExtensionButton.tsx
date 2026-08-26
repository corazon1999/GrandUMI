"use client";

import { GameRequest } from "@/net/GameRequest";
import { useGameStore } from "@/store/gameStore";
import { useLayoutQuarterTurn } from "@/components/ui/ResponsiveScope";
import TurnExtensionIcon from "@/components/game/TurnExtensionIcon";

/** 手机竖屏自动旋转后的控制坞入口；由父级 flex 统一安排位置。 */
export default function MobileTurnExtensionButton() {
  const rotateQuarterTurn = useLayoutQuarterTurn();
  const enabled = useGameStore((s) => s.operationClockEnabled);
  const active = useGameStore((s) => s.operationClockActive);
  const paused = useGameStore((s) => s.operationClockPaused);
  const used = useGameStore((s) => s.myTurnExtensionUsed);
  const isGameOver = useGameStore((s) => s.isGameOver);

  if (!rotateQuarterTurn || !enabled || active !== "my" || paused || used || isGameOver) return null;
  return (
    <button
      type="button"
      onClick={() => GameRequest.requestTurnExtension()}
      data-mobile-turn-extension
      className="flex h-12 w-12 min-h-12 min-w-12 shrink-0 items-center justify-center rounded-full text-amber-100 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-amber-100"
      aria-label="使用本局唯一一次回合加时，增加两分钟"
      title="回合加时 +2:00"
    >
      <span className="flex h-9 w-9 items-center justify-center rounded-full border border-amber-200/60 bg-amber-500/90 text-slate-950 shadow-xl shadow-black/40 transition-colors hover:bg-amber-400">
        <TurnExtensionIcon />
      </span>
    </button>
  );
}
