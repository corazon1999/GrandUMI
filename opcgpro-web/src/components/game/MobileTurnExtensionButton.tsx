"use client";

import { GameRequest } from "@/net/GameRequest";
import { useGameStore } from "@/store/gameStore";

/** 手机竖屏自动旋转后的独立触控入口，不受 1280×720 牌桌二次缩放影响。 */
export default function MobileTurnExtensionButton() {
  const enabled = useGameStore((s) => s.operationClockEnabled);
  const active = useGameStore((s) => s.operationClockActive);
  const paused = useGameStore((s) => s.operationClockPaused);
  const used = useGameStore((s) => s.myTurnExtensionUsed);
  const isGameOver = useGameStore((s) => s.isGameOver);

  if (!enabled || active !== "my" || paused || used || isGameOver) return null;
  return (
    <button
      type="button"
      onClick={() => GameRequest.requestTurnExtension()}
      className="pointer-events-auto fixed z-[65] hidden min-h-12 min-w-32 rounded-xl border border-amber-200/60 bg-amber-500/90 px-4 py-2 text-sm font-black text-slate-950 shadow-xl shadow-black/40 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-amber-100 max-md:block"
      style={{
        right: "calc(0.75rem + var(--layout-safe-right, env(safe-area-inset-right)))",
        top: "calc(50% - 1.5rem)",
      }}
      aria-label="使用本局唯一一次回合加时，增加两分钟"
    >
      加时 +2:00
    </button>
  );
}
