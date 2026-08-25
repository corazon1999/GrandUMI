import type { BattlePhase } from "@/types/game";
import { getBattleTargetMarker } from "@/lib/battleTargetMarker";

export default function BattleTargetBadge({
  phase,
  isBattleTarget,
  isBlocker,
}: {
  phase: BattlePhase;
  isBattleTarget: boolean;
  isBlocker: boolean;
}) {
  const marker = getBattleTargetMarker({ phase, isBattleTarget, isBlocker });
  if (!marker) return null;

  return (
    <span
      role={marker.tone === "under-attack" ? "status" : undefined}
      aria-label={marker.ariaLabel}
      title={marker.ariaLabel}
      data-battle-target-marker={marker.tone}
      className={`pointer-events-none absolute -top-3 left-1/2 z-30 -translate-x-1/2 whitespace-nowrap rounded px-1.5 py-0.5 text-[10px] font-black shadow ${
        marker.tone === "under-attack"
          ? "bg-rose-700 text-white ring-2 ring-white/90"
          : marker.tone === "blocker"
            ? "bg-cyan-300 text-black"
            : "bg-amber-500 text-black"
      }`}
    >
      {marker.text}
    </span>
  );
}
