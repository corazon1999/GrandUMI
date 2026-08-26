"use client";

import { AnimatePresence, motion } from "framer-motion";
import { useEffect, useState } from "react";
import { useGameStore } from "@/store/gameStore";
import { useResponsive } from "@/hooks/useResponsive";
import CardItem from "@/components/ui/CardItem";
import { getGameCard } from "@/data/CardLoader";
import CardBack from "@/components/ui/CardBack";
import GameOverlayPortal from "@/components/ui/GameOverlayPortal";

interface Props {
  side: "my" | "opponent";
}

const pileSizes = {
  sm: "h-[6.3rem] w-[4.5rem]",
  md: "h-[8.4rem] w-[6rem]",
  lg: "h-[11.2rem] w-[8rem]",
};

export default function LifeArea({ side }: Props) {
  const player = useGameStore((s) => (side === "my" ? s.my : s.opponent));
  const { cardSize } = useResponsive();
  const [open, setOpen] = useState(false);
  const count = player?.lifeCount ?? 0;
  const faceUp = player?.lifeFaceUp ?? [];
  const faceUpCount = faceUp.filter((life) => life.faceUp && life.number).length;
  const canInspect = faceUpCount > 0;
  const visibleCards = Math.min(Math.max(count, 1), 5);
  const topCardOffset = count > 0 ? (visibleCards - 1) * 4 : 0;

  useEffect(() => {
    if (!canInspect) setOpen(false);
  }, [canInspect]);

  useEffect(() => {
    if (!open) return;
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setOpen(false);
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [open]);

  const openInspector = () => {
    if (canInspect) setOpen(true);
  };

  return (
    <>
      <div
        className={`relative rounded-md outline-none transition-shadow ${pileSizes[cardSize]} ${
          canInspect
            ? "cursor-pointer ring-2 ring-amber-300/45 hover:ring-amber-200 focus-visible:ring-amber-200"
            : ""
        }`}
        data-zone="life"
        data-zone-side={side}
        onClick={openInspector}
        onKeyDown={(event) => {
          if (!canInspect || (event.key !== "Enter" && event.key !== " ")) return;
          event.preventDefault();
          setOpen(true);
        }}
        role={canInspect ? "button" : undefined}
        tabIndex={canInspect ? 0 : undefined}
        title={canInspect ? `查看${side === "my" ? "我方" : "对手"}生命区公开牌` : undefined}
        aria-label={canInspect ? `查看${side === "my" ? "我方" : "对手"}生命区，${faceUpCount} 张正面牌` : undefined}
      >
        <span className="absolute left-2 top-2 z-20 text-[11px] font-semibold text-slate-200 drop-shadow">
          {side === "my" ? "生命" : "对手生命"}
        </span>
        {count > 0 ? (
          Array.from({ length: visibleCards }).map((_, i) => {
            const info = faceUp[i];
            const isFaceUp = !!info?.faceUp && !!info?.number;
            return (
              <div
                key={i}
                className="absolute"
                style={{
                  inset: 0,
                  transform: `translate(${i * 4}px, ${i * 4}px)`,
                  // 正面朝上的牌为公开信息，提到背面牌之上以便看清（反馈 #116）
                  zIndex: isFaceUp ? 10 + i : i,
                }}
              >
                {isFaceUp ? (
                  <CardItem
                    card={getGameCard(info!.number!, player?.spriteMap) ?? null}
                    size={cardSize}
                    hideCounter
                    hidePower
                    hideCost
                    liftOnSelect={false}
                  />
                ) : (
                  <CardBack cardBackId={player?.cardBackId} side={side} className="shadow-xl shadow-black/35" />
                )}
              </div>
            );
          })
        ) : (
          <div className="h-full w-full rounded-md border-2 border-dashed border-slate-500/60 bg-slate-950/45" />
        )}
        <div
          className="absolute z-30 flex h-8 min-w-8 items-center justify-center rounded-md border border-white/20 bg-slate-950 px-1 text-base font-black text-white shadow"
          style={{
            left: `calc(50% + ${topCardOffset}px)`,
            top: topCardOffset - 12,
            transform: "translateX(-50%)",
          }}
        >
          {count}
        </div>
      </div>

      <GameOverlayPortal>
        <AnimatePresence>
          {open && (
            <motion.div
              className="pointer-events-auto fixed inset-0 z-[110] flex flex-col items-center justify-center gap-4 overflow-y-auto bg-black/80 px-[calc(1rem+var(--layout-safe-left,0px))] py-[calc(1rem+var(--layout-safe-top,0px))] [padding-bottom:calc(1rem+var(--layout-safe-bottom,0px))] [padding-right:calc(1rem+var(--layout-safe-right,0px))] @[640px]:gap-6"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              onClick={() => setOpen(false)}
            >
                <div className="flex flex-wrap items-center justify-center gap-3">
                  <p className="text-center text-lg font-bold text-white">
                    {side === "my" ? "我方" : "对手"}生命区（{count} 张，{faceUpCount} 张正面）
                  </p>
                  <button
                    type="button"
                    onClick={() => setOpen(false)}
                    className="min-h-12 rounded-lg bg-gray-600 px-4 py-2 text-sm font-bold text-white hover:bg-gray-500"
                  >
                    关闭
                  </button>
                </div>

                <div
                  className="flex max-h-[75cqh] max-w-[calc(100cqw-2rem)] flex-wrap justify-center gap-3 overflow-y-auto p-2"
                  onClick={(event) => event.stopPropagation()}
                >
                  {Array.from({ length: count }).map((_, index) => {
                    const info = faceUp[index];
                    const isFaceUp = !!info?.faceUp && !!info?.number;
                    const position =
                      index === 0 ? "最上方" : index === count - 1 ? "最下方" : `第 ${index + 1} 张`;
                    return (
                      <div key={`${info?.number ?? "hidden"}-${index}`} className="flex flex-col items-center gap-1.5">
                        <CardItem
                          card={isFaceUp ? getGameCard(info!.number!, player?.spriteMap) ?? null : null}
                          faceDown={!isFaceUp}
                          cardBackId={player?.cardBackId}
                          cardBackSide={side}
                          size="md"
                          hideCounter
                          hidePower
                          hideCost
                          liftOnSelect={false}
                        />
                        <span className="text-xs font-bold text-slate-300">{position}</span>
                      </div>
                    );
                  })}
                </div>
            </motion.div>
          )}
        </AnimatePresence>
      </GameOverlayPortal>
    </>
  );
}
