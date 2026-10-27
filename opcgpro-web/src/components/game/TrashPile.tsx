"use client";

import { motion, AnimatePresence } from "framer-motion";
import { useState } from "react";
import { useGameStore } from "@/store/gameStore";
import { useResponsive } from "@/hooks/useResponsive";
import { getCard, getGameCard } from "@/data/CardLoader";
import { sortCardDisplayIndices } from "@/lib/cardDisplaySort";
import CardItem from "@/components/ui/CardItem";
import GameOverlayPortal from "@/components/ui/GameOverlayPortal";

interface Props {
  side: "my" | "opponent";
}

// 稳定的空数组引用：避免在 selector 里用 `?? []` 每次返回新数组，
// 否则游戏结束/重置后 trashNumbers 为 undefined 时会触发 React
// "getSnapshot should be cached" 无限循环报错。
const EMPTY: readonly string[] = [];

const pileSizes = {
  sm: "h-[6.3rem] w-[4.5rem]",
  md: "h-[8.4rem] w-[6rem]",
  lg: "h-[11.2rem] w-[8rem]",
};

export default function TrashPile({ side }: Props) {
  // 订阅完整墓地数组：数组最后一张 = 最近送入墓地的卡（服务端 Trash.Add 逐张追加）
  const trash = useGameStore((s) => (side === "my" ? s.my?.trashNumbers : s.opponent?.trashNumbers) ?? EMPTY);
  const spriteMap = useGameStore((s) => (side === "my" ? s.my?.spriteMap : s.opponent?.spriteMap));
  const { cardSize } = useResponsive();
  const [open, setOpen] = useState(false);
  const [sortByCost, setSortByCost] = useState(false);

  const count = trash.length;
  const topNumber = count > 0 ? trash[count - 1] : null; // 封面 = 最近送入的卡
  const topCard = topNumber ? getGameCard(topNumber, spriteMap) ?? null : null;
  const displayIndices = sortByCost
    ? sortCardDisplayIndices(trash, (number) => getCard(number)?.cost)
    : Array.from({ length: count }, (_, index) => count - 1 - index);

  return (
    <div className="flex flex-col items-center gap-2 rounded-md border border-zinc-300/15 bg-black/30 px-2.5 py-2 shadow-lg shadow-black/25">
      <span className="text-[11px] font-semibold text-slate-300">墓地</span>
      <div
        className={`relative cursor-pointer ${pileSizes[cardSize]}`}
        data-zone="trash"
        data-zone-side={side}
        onClick={() => setOpen(true)}
        title={count > 0 ? `查看墓地（${count} 张）` : "墓地为空"}
      >
        {topCard ? (
          // 封面 = 最近送入的卡，复用 CardItem；点击冒泡到外层容器展开墓地
          <CardItem card={topCard} size={cardSize} />
        ) : (
          <div className="flex h-full w-full items-center justify-center rounded-md border-2 border-dashed border-zinc-400/35 bg-zinc-950/60">
            <span className="text-xs font-black text-zinc-500">TRASH</span>
          </div>
        )}
        <div className="absolute -right-3 -top-3 flex h-8 min-w-8 items-center justify-center rounded-md border border-white/20 bg-slate-950 px-1 text-base font-black text-white shadow">
          {count}
        </div>
      </div>

      <GameOverlayPortal>
        <AnimatePresence>
          {open && (
            <motion.div
              className="pointer-events-auto fixed inset-0 z-50 flex flex-col items-center justify-center gap-4 overflow-y-auto bg-black/80 px-[calc(1rem+var(--layout-safe-left,0px))] py-[calc(1rem+var(--layout-safe-top,0px))] [padding-bottom:calc(1rem+var(--layout-safe-bottom,0px))] [padding-right:calc(1rem+var(--layout-safe-right,0px))]"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              onClick={() => setOpen(false)}
            >
                <div className="flex shrink-0 flex-wrap items-center justify-center gap-3" onClick={(event) => event.stopPropagation()}>
                  <p className="text-lg font-bold text-white">
                    {side === "my" ? "我方" : "对手"}墓地（{count} 张）
                  </p>
                  <button
                    type="button"
                    data-sort-trash={side}
                    aria-pressed={sortByCost}
                    disabled={count < 2}
                    title={sortByCost ? "恢复最近进入墓地的卡排在最前" : "按卡面费用从低到高整理，相同卡号放在一起"}
                    onClick={() => setSortByCost((current) => !current)}
                    className="min-h-12 min-w-12 rounded-lg bg-sky-700 px-4 py-2 text-sm font-bold text-white hover:bg-sky-600 disabled:cursor-not-allowed disabled:opacity-40"
                  >
                    {sortByCost ? "恢复时间顺序" : "一键整理"}
                  </button>
                  <button
                    type="button"
                    onClick={() => setOpen(false)}
                    className="min-h-12 rounded-lg bg-gray-600 px-4 py-2 text-sm font-bold text-white hover:bg-gray-500"
                  >
                    关闭
                  </button>
                </div>

                <div
                  data-trash-cards={side}
                  className="flex min-h-0 max-h-[75cqh] max-w-[calc(100cqw-2rem-var(--layout-safe-left,0px)-var(--layout-safe-right,0px))] flex-wrap justify-center gap-2 overflow-y-auto p-2"
                  onClick={(e) => e.stopPropagation()}
                >
                  {count === 0 ? (
                    <span className="text-sm text-gray-400">墓地为空</span>
                  ) : (
                    displayIndices.map((index) => (
                      <div key={`${trash[index]}-${index}`} data-trash-index={index}>
                        <CardItem card={getGameCard(trash[index], spriteMap) ?? null} size="md" />
                      </div>
                    ))
                  )}
                </div>
            </motion.div>
          )}
        </AnimatePresence>
      </GameOverlayPortal>
    </div>
  );
}
