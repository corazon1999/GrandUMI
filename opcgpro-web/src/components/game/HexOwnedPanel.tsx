"use client";

import { useGameStore } from "@/store/gameStore";
import type { HexDefinitionSnapshot, HexTierSnapshot } from "@/types/net";

const TIER_DOT: Record<HexTierSnapshot, string> = {
  Silver: "bg-slate-300",
  Gold: "bg-amber-300",
  Rainbow: "bg-gradient-to-br from-fuchsia-300 to-cyan-300",
};

function OwnedList({ label, items }: { label: string; items: HexDefinitionSnapshot[] }) {
  return (
    <div>
      <p className="text-[9px] font-black tracking-wide text-slate-400">{label}</p>
      {items.length > 0 ? (
        <ul className="mt-1 space-y-1">
          {items.map((hex) => (
            <li
              key={hex.id}
              className="flex min-h-6 items-center gap-1.5 rounded border border-white/10 bg-black/20 px-1.5 py-1 text-[10px] font-bold leading-3 text-slate-100"
              title={`${hex.name}：${hex.description}`}
              aria-label={`${hex.name}：${hex.description}`}
            >
              <span className={`h-2 w-2 shrink-0 rounded-full ${TIER_DOT[hex.tier]}`} />
              <span className="line-clamp-2">{hex.name}</span>
            </li>
          ))}
        </ul>
      ) : (
        <p className="mt-1 text-[9px] text-slate-600">尚未获得</p>
      )}
    </div>
  );
}

export default function HexOwnedPanel() {
  const hexState = useGameStore((state) => state.hexState);
  if (!hexState) return null;

  return (
    <section
      data-hex-owned-panel
      className="max-h-52 overflow-y-auto rounded-md border border-cyan-300/20 bg-slate-950/70 p-2 shadow-inner shadow-black/30"
      aria-label="本局已拥有的海克斯"
    >
      <h2 className="mb-2 text-[11px] font-black text-cyan-200">本局海克斯</h2>
      <div className="space-y-2">
        <OwnedList label="对手" items={hexState.opponentOwned} />
        <div className="h-px bg-white/10" />
        <OwnedList label="我" items={hexState.myOwned} />
      </div>
    </section>
  );
}
