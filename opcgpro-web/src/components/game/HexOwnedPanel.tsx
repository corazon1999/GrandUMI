"use client";

import { useEffect, useId, useRef, useState, type PointerEvent as ReactPointerEvent } from "react";
import type { HexDefinitionSnapshot, HexTierSnapshot } from "@/types/net";

const MAX_OWNED_HEXES = 3;

const TIER_META: Record<HexTierSnapshot, { label: string; slot: string; text: string; badge: string }> = {
  Silver: {
    label: "银",
    slot: "border-slate-200/70 bg-gradient-to-br from-slate-100/30 via-slate-400/15 to-slate-950/90 text-slate-100 shadow-[inset_0_0_10px_rgba(226,232,240,0.2)]",
    text: "text-slate-100",
    badge: "border-slate-200/35 bg-slate-300/10 text-slate-100",
  },
  Gold: {
    label: "金",
    slot: "border-amber-200/75 bg-gradient-to-br from-amber-100/35 via-amber-400/20 to-amber-950/90 text-amber-100 shadow-[inset_0_0_10px_rgba(252,211,77,0.22)]",
    text: "text-amber-100",
    badge: "border-amber-200/35 bg-amber-300/10 text-amber-100",
  },
  Rainbow: {
    label: "彩",
    slot: "border-fuchsia-200/75 bg-gradient-to-br from-fuchsia-300/35 via-violet-400/20 to-cyan-300/35 text-white shadow-[inset_0_0_12px_rgba(103,232,249,0.25)]",
    text: "text-fuchsia-100",
    badge: "border-fuchsia-200/35 bg-gradient-to-r from-fuchsia-400/15 to-cyan-300/15 text-fuchsia-100",
  },
};

function handleDesktopPointer(
  event: ReactPointerEvent<HTMLButtonElement>,
  callback: () => void,
) {
  if (event.pointerType === "mouse") callback();
}

export default function HexOwnedSlots({
  side,
  label,
  items,
}: {
  side: "my" | "opponent";
  label: string;
  items: HexDefinitionSnapshot[];
}) {
  const rootRef = useRef<HTMLDivElement>(null);
  const popoverId = useId();
  const [hoveredIndex, setHoveredIndex] = useState<number | null>(null);
  const [focusedIndex, setFocusedIndex] = useState<number | null>(null);
  const [pinnedIndex, setPinnedIndex] = useState<number | null>(null);
  const owned = items.slice(0, MAX_OWNED_HEXES);
  const activeIndex = pinnedIndex ?? focusedIndex ?? hoveredIndex;
  const activeHex = activeIndex === null ? null : owned[activeIndex] ?? null;
  const activeTier = activeHex ? TIER_META[activeHex.tier] : null;

  useEffect(() => {
    if (pinnedIndex === null) return;

    const closeOnOutsidePointer = (event: PointerEvent) => {
      if (event.target instanceof Node && !rootRef.current?.contains(event.target)) {
        setPinnedIndex(null);
      }
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") setPinnedIndex(null);
    };

    document.addEventListener("pointerdown", closeOnOutsidePointer);
    document.addEventListener("keydown", closeOnEscape);
    return () => {
      document.removeEventListener("pointerdown", closeOnOutsidePointer);
      document.removeEventListener("keydown", closeOnEscape);
    };
  }, [pinnedIndex]);

  useEffect(() => {
    if (pinnedIndex !== null && pinnedIndex >= owned.length) {
      setPinnedIndex(null);
    }
  }, [owned.length, pinnedIndex]);

  return (
    <div ref={rootRef} className="contents" data-hex-owned-slots={side}>
      <ol
        className="flex shrink-0 [&>li+li]:-ml-px"
        aria-label={`${label}已获得海克斯，3 个槽位`}
      >
        {Array.from({ length: MAX_OWNED_HEXES }, (_, index) => {
          const hex = owned[index];
          const tier = hex ? TIER_META[hex.tier] : null;
          const isActive = activeIndex === index && Boolean(hex);
          return (
            <li key={hex ? `${side}-${hex.id}-${index}` : `${side}-empty-${index}`}>
              <button
                type="button"
                data-hex-owned-slot={side}
                data-hex-slot-index={index + 1}
                data-hex-slot-state={hex ? "owned" : "empty"}
                data-hex-tier={hex?.tier}
                disabled={!hex}
                className={`relative flex h-11 min-h-11 w-11 min-w-11 items-center justify-center rounded-md border text-[11px] font-black outline-none transition-colors enabled:cursor-pointer enabled:hover:z-10 enabled:hover:brightness-125 enabled:focus-visible:z-10 enabled:focus-visible:ring-2 enabled:focus-visible:ring-cyan-200 disabled:cursor-default ${
                  tier
                    ? tier.slot
                    : "border-slate-500/20 bg-slate-950/35 text-slate-600 shadow-inner shadow-black/25"
                }`}
                aria-label={hex
                  ? `${label}第 ${index + 1} 个海克斯，${tier?.label}品质“${hex.name}”，查看完整效果`
                  : `${label}第 ${index + 1} 个海克斯空槽`}
                aria-controls={hex ? popoverId : undefined}
                aria-describedby={isActive ? popoverId : undefined}
                aria-expanded={hex ? pinnedIndex === index : undefined}
                onPointerEnter={(event) => handleDesktopPointer(event, () => setHoveredIndex(index))}
                onPointerLeave={(event) => handleDesktopPointer(event, () => setHoveredIndex(null))}
                onFocus={() => setFocusedIndex(index)}
                onBlur={() => setFocusedIndex(null)}
                onClick={(event) => {
                  if (!hex) return;
                  if (pinnedIndex === index) {
                    setPinnedIndex(null);
                    setFocusedIndex(null);
                    event.currentTarget.blur();
                    return;
                  }
                  setPinnedIndex(index);
                }}
              >
                <span
                  aria-hidden="true"
                  className={`flex h-8 w-8 items-center justify-center [clip-path:polygon(25%_7%,75%_7%,100%_50%,75%_93%,25%_93%,0_50%)] ${
                    tier ? "bg-black/25" : "bg-slate-700/15"
                  }`}
                >
                  {tier ? tier.label : "·"}
                </span>
                {hex && <span className="sr-only">{hex.name}</span>}
              </button>
            </li>
          );
        })}
      </ol>

      {activeHex && activeTier && (
        <section
          id={popoverId}
          data-hex-owned-popover={side}
          role={pinnedIndex === activeIndex ? "dialog" : "tooltip"}
          aria-label={`${activeHex.name}海克斯详情`}
          className="absolute inset-x-0 top-[calc(100%+0.25rem)] z-[70] overflow-y-auto overscroll-contain rounded-lg border border-cyan-200/35 bg-slate-950/98 p-2.5 text-left shadow-2xl shadow-black/70"
          style={{
            maxHeight: "min(12rem, calc(100cqh - 1rem - var(--layout-safe-top, 0px) - var(--layout-safe-bottom, 0px)))",
          }}
        >
          <div className="flex items-start gap-1.5">
            <div className="min-w-0 flex-1">
              <h3 className={`break-words text-xs font-black leading-4 ${activeTier.text}`}>
                {activeHex.name}
              </h3>
              <span className={`mt-1 inline-flex rounded border px-1.5 py-0.5 text-[9px] font-black ${activeTier.badge}`}>
                {activeTier.label}品质
              </span>
            </div>
            {pinnedIndex === activeIndex && (
              <button
                type="button"
                className="flex h-11 min-h-11 w-11 min-w-11 shrink-0 items-center justify-center rounded-md border border-white/15 bg-white/5 text-lg font-black text-slate-200 hover:bg-white/10 focus-visible:outline-2 focus-visible:outline-cyan-200"
                aria-label={`关闭${activeHex.name}海克斯详情`}
                onClick={() => {
                  setPinnedIndex(null);
                  setFocusedIndex(null);
                  setHoveredIndex(null);
                }}
              >
                ×
              </button>
            )}
          </div>
          <p className="mt-2 whitespace-pre-wrap break-words border-t border-white/10 pt-2 text-[10px] font-semibold leading-4 text-slate-200">
            {activeHex.description}
          </p>
        </section>
      )}
    </div>
  );
}
