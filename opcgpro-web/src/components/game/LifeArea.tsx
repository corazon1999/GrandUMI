"use client";

import { useGameStore } from "@/store/gameStore";
import { useResponsive } from "@/hooks/useResponsive";
import CardItem from "@/components/ui/CardItem";
import { getCard } from "@/data/CardLoader";

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
  const count = player?.lifeCount ?? 0;
  const faceUp = player?.lifeFaceUp ?? [];
  const visibleCards = Math.min(Math.max(count, 1), 5);

  return (
    <div className={`relative ${pileSizes[cardSize]}`}>
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
                  card={getCard(info!.number!) ?? null}
                  size={cardSize}
                  hideCounter
                  hidePower
                  hideCost
                  liftOnSelect={false}
                />
              ) : (
                <div className="relative h-full w-full rounded-md border-2 border-red-200/35 bg-gradient-to-br from-red-800 via-rose-950 to-slate-950 shadow-xl shadow-black/35">
                  <div className="absolute inset-2 rounded border border-red-100/15" />
                </div>
              )}
            </div>
          );
        })
      ) : (
        <div className="h-full w-full rounded-md border-2 border-dashed border-slate-500/60 bg-slate-950/45" />
      )}
      <div className="absolute -right-4 -top-3 z-30 flex h-8 min-w-8 items-center justify-center rounded-md border border-white/20 bg-slate-950 px-1 text-base font-black text-white shadow">
        {count}
      </div>
    </div>
  );
}
