"use client";

import { useCallback, useEffect, useId, useRef, useState, type PointerEvent as ReactPointerEvent } from "react";
import GameOverlayPortal from "@/components/ui/GameOverlayPortal";
import { buildOwnedHexPresentation } from "@/game/hexOwnedPresentation.mjs";
import type { HexDefinitionSnapshot, HexTierSnapshot } from "@/types/net";

const MAX_VISIBLE_OWNED_HEXES = 3;

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
    label: "棱彩",
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
  const allTriggerRef = useRef<HTMLButtonElement>(null);
  const allDialogRef = useRef<HTMLElement>(null);
  const popoverId = useId();
  const allDialogId = useId();
  const allDialogHeadingId = useId();
  const [hoveredIndex, setHoveredIndex] = useState<number | null>(null);
  const [focusedIndex, setFocusedIndex] = useState<number | null>(null);
  const [pinnedIndex, setPinnedIndex] = useState<number | null>(null);
  const [allOpen, setAllOpen] = useState(false);
  const { visibleItems: owned, overflowCount } = buildOwnedHexPresentation(
    items,
    MAX_VISIBLE_OWNED_HEXES,
  );
  const activeIndex = pinnedIndex ?? focusedIndex ?? hoveredIndex;
  const activeHex = activeIndex === null ? null : owned[activeIndex] ?? null;
  const activeTier = activeHex ? TIER_META[activeHex.tier] : null;
  const activeTierLabel = activeHex?.tierLabel ?? activeTier?.label ?? null;

  const closeAll = useCallback(() => setAllOpen(false), []);

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

  useEffect(() => {
    if (overflowCount === 0 && allOpen) closeAll();
  }, [allOpen, closeAll, overflowCount]);

  useEffect(() => {
    if (!allOpen) return;
    const restoreFocusTo = allTriggerRef.current;
    const frame = requestAnimationFrame(() => allDialogRef.current?.focus());

    const handleDialogKeys = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        closeAll();
        return;
      }
      if (event.key !== "Tab" || !allDialogRef.current) return;
      const focusable = Array.from(allDialogRef.current.querySelectorAll<HTMLElement>(
        'button:not([disabled]), [href], [tabindex]:not([tabindex="-1"])',
      ));
      if (focusable.length === 0) {
        event.preventDefault();
        allDialogRef.current.focus();
        return;
      }
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && (document.activeElement === first || document.activeElement === allDialogRef.current)) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener("keydown", handleDialogKeys);
    return () => {
      cancelAnimationFrame(frame);
      document.removeEventListener("keydown", handleDialogKeys);
      restoreFocusTo?.focus();
    };
  }, [allOpen, closeAll]);

  return (
    <div ref={rootRef} className="relative flex shrink-0 flex-col items-end gap-1" data-hex-owned-slots={side}>
      <ol
        className="flex shrink-0 [&>li+li]:-ml-px"
        aria-label={`${label}已获得海克斯，3 个固定槽位`}
      >
        {Array.from({ length: MAX_VISIBLE_OWNED_HEXES }, (_, index) => {
          const hex = owned[index];
          const tier = hex ? TIER_META[hex.tier] : null;
          const tierLabel = hex?.tierLabel ?? tier?.label;
          const isActive = activeIndex === index && Boolean(hex);
          return (
            <li key={hex ? `${side}-${hex.id}-${index}` : `${side}-empty-${index}`}>
              <button
                type="button"
                data-hex-owned-slot={side}
                data-hex-slot-index={index + 1}
                data-hex-slot-state={hex ? "owned" : "empty"}
                data-hex-slot-visible-label={hex ? "name" : "empty"}
                data-hex-tier={hex?.tier}
                disabled={!hex}
                className={`relative flex h-11 min-h-11 w-11 min-w-11 items-center justify-center rounded-md border text-[11px] font-black outline-none transition-colors enabled:cursor-pointer enabled:hover:z-10 enabled:hover:brightness-125 enabled:focus-visible:z-10 enabled:focus-visible:ring-2 enabled:focus-visible:ring-cyan-200 disabled:cursor-default ${
                  tier
                    ? tier.slot
                    : "border-slate-500/20 bg-slate-950/35 text-slate-600 shadow-inner shadow-black/25"
                }`}
                aria-label={hex
                  ? `${label}第 ${index + 1} 个海克斯，${tierLabel}品质“${hex.name}”，查看完整效果`
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
                {hex ? (
                  <span
                    aria-hidden="true"
                    className="line-clamp-3 w-full break-all px-0.5 text-center text-[10px] leading-[11px]"
                  >
                    {hex.name}
                  </span>
                ) : (
                  <span
                    aria-hidden="true"
                    className="flex h-8 w-8 items-center justify-center bg-slate-700/15 [clip-path:polygon(25%_7%,75%_7%,100%_50%,75%_93%,25%_93%,0_50%)]"
                  >
                    ·
                  </span>
                )}
              </button>
            </li>
          );
        })}
      </ol>

      {overflowCount > 0 && (
        <button
          ref={allTriggerRef}
          type="button"
          data-hex-owned-overflow-trigger={side}
          className="flex h-12 min-h-12 w-full min-w-12 items-center justify-center gap-1 rounded-md border border-cyan-200/35 bg-slate-950/85 px-2 text-[10px] font-black text-cyan-100 shadow-lg shadow-black/30 hover:bg-cyan-300/10 focus-visible:outline-2 focus-visible:outline-cyan-200"
          aria-label={`${label}另有 ${overflowCount} 个海克斯，查看全部 ${items.length} 个`}
          aria-controls={allDialogId}
          aria-expanded={allOpen}
          onClick={() => {
            setHoveredIndex(null);
            setFocusedIndex(null);
            setPinnedIndex(null);
            setAllOpen(true);
          }}
        >
          <span className="text-sm">+{overflowCount}</span>
          <span>查看全部</span>
        </button>
      )}

      {!allOpen && activeHex && activeTier && activeTierLabel && (
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
                {activeTierLabel}品质
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

      {allOpen && (
        <GameOverlayPortal>
          <div
            data-hex-owned-all-backdrop={side}
            className="pointer-events-auto fixed inset-0 z-[95] flex items-center justify-center bg-slate-950/80"
            style={{
              paddingTop: "calc(0.75rem + var(--layout-safe-top, 0px))",
              paddingRight: "calc(0.75rem + var(--layout-safe-right, 0px))",
              paddingBottom: "calc(0.75rem + var(--layout-safe-bottom, 0px))",
              paddingLeft: "calc(0.75rem + var(--layout-safe-left, 0px))",
            }}
            onPointerDown={(event) => {
              if (event.target === event.currentTarget) closeAll();
            }}
          >
            <section
              ref={allDialogRef}
              id={allDialogId}
              data-hex-owned-all-dialog={side}
              role="dialog"
              aria-modal="true"
              aria-labelledby={allDialogHeadingId}
              tabIndex={-1}
              className="flex w-full max-w-[38rem] flex-col overflow-hidden rounded-xl border border-cyan-200/35 bg-slate-950/98 text-left shadow-2xl shadow-black/80 outline-none focus-visible:ring-2 focus-visible:ring-cyan-200"
              style={{
                maxHeight: "calc(100cqh - 1.5rem - var(--layout-safe-top, 0px) - var(--layout-safe-bottom, 0px))",
              }}
            >
              <header className="flex shrink-0 items-center gap-3 border-b border-white/10 p-3">
                <div className="min-w-0 flex-1">
                  <h2 id={allDialogHeadingId} className="text-sm font-black text-cyan-100">
                    {label}全部海克斯
                  </h2>
                  <p className="mt-0.5 text-[10px] font-semibold text-slate-400">
                    按获得顺序展示，共 {items.length} 个
                  </p>
                </div>
                <button
                  type="button"
                  className="flex h-12 min-h-12 w-12 min-w-12 shrink-0 items-center justify-center rounded-md border border-white/15 bg-white/5 text-xl font-black text-slate-200 hover:bg-white/10 focus-visible:outline-2 focus-visible:outline-cyan-200"
                  aria-label={`关闭${label}全部海克斯`}
                  onClick={closeAll}
                >
                  ×
                </button>
              </header>
              <ol
                data-hex-owned-all-list={side}
                className="grid min-h-0 flex-1 touch-pan-y grid-cols-1 gap-2 overflow-y-auto overscroll-contain p-3 @[640px]:grid-cols-2"
              >
                {items.map((hex, index) => {
                  const tier = TIER_META[hex.tier];
                  const tierLabel = hex.tierLabel ?? tier.label;
                  return (
                    <li
                      key={`${side}-all-${hex.id}-${index}`}
                      data-hex-owned-all-item={side}
                      data-hex-tier={hex.tier}
                      className={`rounded-lg border p-3 ${tier.slot}`}
                    >
                      <div className="flex items-start justify-between gap-2">
                        <h3 className={`min-w-0 break-words text-xs font-black leading-4 ${tier.text}`}>
                          {hex.name}
                        </h3>
                        <span className={`shrink-0 rounded border px-1.5 py-0.5 text-[9px] font-black ${tier.badge}`}>
                          {tierLabel}品质
                        </span>
                      </div>
                      <p className="mt-2 whitespace-pre-wrap break-words border-t border-white/10 pt-2 text-[10px] font-semibold leading-4 text-slate-100">
                        {hex.description}
                      </p>
                    </li>
                  );
                })}
              </ol>
            </section>
          </div>
        </GameOverlayPortal>
      )}
    </div>
  );
}
