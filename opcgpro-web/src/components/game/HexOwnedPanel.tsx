"use client";

import { useGameStore } from "@/store/gameStore";
import type { HexDefinitionSnapshot, HexTierSnapshot } from "@/types/net";

const TIER_META: Record<HexTierSnapshot, { label: string; dot: string; text: string }> = {
  Silver: { label: "银", dot: "bg-slate-300", text: "text-slate-200" },
  Gold: { label: "金", dot: "bg-amber-300", text: "text-amber-200" },
  Rainbow: { label: "彩", dot: "bg-gradient-to-br from-fuchsia-300 to-cyan-300", text: "text-fuchsia-200" },
};

function OwnedList({ label, items }: { label: string; items: HexDefinitionSnapshot[] }) {
  return (
    <section aria-label={`${label}已获得海克斯`}>
      <h3 className="text-[10px] font-black tracking-wide text-slate-300">{label}</h3>
      {items.length > 0 ? (
        <ul className="mt-1 space-y-1.5">
          {items.map((hex, index) => {
            const tier = TIER_META[hex.tier];
            return (
              <li key={`${hex.id}-${index}`}>
                <details className="group rounded-md border border-white/10 bg-black/25 open:border-cyan-300/25 open:bg-slate-900/80">
                  <summary
                    className="flex min-h-12 cursor-pointer list-none items-center gap-2 rounded-md px-2 py-1.5 text-[11px] font-black leading-4 text-slate-100 outline-none hover:bg-white/5 focus-visible:ring-2 focus-visible:ring-cyan-300 [&::-webkit-details-marker]:hidden"
                    aria-label={`${tier.label}色海克斯“${hex.name}”，展开查看完整效果`}
                  >
                    <span className={`h-2.5 w-2.5 shrink-0 rounded-full ${tier.dot}`} />
                    <span className="min-w-0 flex-1 break-words">{hex.name}</span>
                    <span className={`shrink-0 text-[9px] ${tier.text}`}>{tier.label}</span>
                    <span className="shrink-0 text-xs text-cyan-200 transition-transform group-open:rotate-90" aria-hidden="true">›</span>
                  </summary>
                  <p className="border-t border-white/10 px-2 py-2 text-[10px] font-semibold leading-4 text-slate-200">
                    {hex.description}
                  </p>
                </details>
              </li>
            );
          })}
        </ul>
      ) : (
        <p className="mt-1 min-h-6 text-[10px] text-slate-500">尚未获得</p>
      )}
    </section>
  );
}

export default function HexOwnedPanel() {
  const hexState = useGameStore((state) => state.hexState);
  if (!hexState) return null;

  return (
    <section
      data-hex-owned-panel
      data-hex-details-panel
      className="min-h-0 flex-1 overflow-y-auto overscroll-contain rounded-md border border-cyan-300/20 bg-slate-950/80 p-2 shadow-inner shadow-black/30"
      aria-label="双方海克斯详情"
    >
      <div className="sticky top-0 z-10 -mx-2 -mt-2 mb-2 border-b border-cyan-300/15 bg-slate-950/95 px-2 py-2">
        <h2 className="text-[11px] font-black text-cyan-100">海克斯详情</h2>
        {(hexState.tierSequence ?? []).length === 3 && (
          <div className="mt-1 flex gap-1" aria-label={`共享品质序列：${hexState.tierSequence.map((tier) => TIER_META[tier].label).join("、")}`}>
            {hexState.tierSequence.map((tier, index) => (
              <span key={`${tier}-${index}`} className={`flex min-h-6 min-w-6 items-center justify-center rounded border border-white/10 bg-black/30 px-1 text-[9px] font-black ${TIER_META[tier].text}`}>
                {TIER_META[tier].label}
              </span>
            ))}
          </div>
        )}
      </div>
      <div className="space-y-3">
        <OwnedList label="我方" items={hexState.myOwned} />
        <div className="h-px bg-white/10" />
        <OwnedList label="对方" items={hexState.opponentOwned} />
      </div>
    </section>
  );
}
