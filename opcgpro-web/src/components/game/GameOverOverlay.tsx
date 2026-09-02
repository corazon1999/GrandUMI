"use client";

import { AnimatePresence, motion } from "framer-motion";
import { useEffect, useState } from "react";
import RankResultPanel from "@/components/game/RankResultPanel";
import { useGameStore } from "@/store/gameStore";
import { useNetStore } from "@/store/netStore";

interface Props {
  isObserver: boolean;
  onReturnToHome: () => void;
}

export default function GameOverOverlay({ isObserver, onReturnToHome }: Props) {
  const [hidden, setHidden] = useState(false);
  const isGameOver = useGameStore((state) => state.isGameOver);
  const settlementReady = useGameStore((state) => state.cinematic.settlementReady);
  const isDraw = useGameStore((state) => state.isDraw);
  const winnerIsMe = useGameStore((state) => state.winnerIsMe);
  const gameOverReason = useGameStore((state) => state.gameOverReason);
  const matchKind = useGameStore((state) => state.matchKind);
  const rankResult = useNetStore((state) => state.lastRankResult);

  useEffect(() => {
    if (!isGameOver) setHidden(false);
  }, [isGameOver]);

  if (!isGameOver || !settlementReady) return null;

  return (
    <>
      <AnimatePresence>
        {!hidden && (
          <motion.div
            className="fixed inset-0 z-40 flex flex-col items-center overflow-y-auto bg-black/70 px-[calc(1rem+var(--layout-safe-left,env(safe-area-inset-left)))] py-[calc(1rem+var(--layout-safe-top,env(safe-area-inset-top)))]"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
          >
            <motion.h1
              className={
                isObserver
                  ? "text-5xl font-black text-purple-300 drop-shadow-[0_0_12px_rgba(216,180,254,0.5)]"
                  : isDraw
                    ? "text-5xl font-black text-sky-300 drop-shadow-[0_0_12px_rgba(125,211,252,0.5)]"
                    : winnerIsMe
                      ? "text-5xl font-black text-yellow-400 drop-shadow-[0_0_12px_rgba(250,204,21,0.6)]"
                      : "text-5xl font-black text-gray-400 drop-shadow-[0_0_12px_rgba(156,163,175,0.5)]"
              }
              initial={{ scale: 0.6, opacity: 0 }}
              animate={{ scale: 1, opacity: 1 }}
              transition={{ delay: 0.2, type: "spring", stiffness: 200 }}
            >
              {isObserver ? "对局结束" : isDraw ? "本局平局" : winnerIsMe ? "你胜利了！" : "你战败了"}
            </motion.h1>
            {gameOverReason && (
              <motion.p
                className="mt-3 text-lg text-white/70"
                initial={{ opacity: 0, y: 8 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.5 }}
              >
                结束原因：{gameOverReason}
              </motion.p>
            )}
            {!isDraw && (matchKind === "Ranked" || matchKind === "RankedWild") && rankResult && (
              <motion.div
                className="contents"
                initial={{ opacity: 0, y: 8 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.6 }}
              >
                <RankResultPanel result={rankResult} />
              </motion.div>
            )}
            <motion.div
              className="mt-4 flex flex-wrap items-center justify-center gap-3"
              initial={{ opacity: 0, y: 12 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.7 }}
            >
              <button
                type="button"
                onClick={() => setHidden(true)}
                className="h-12 min-w-28 rounded-lg border border-white/30 bg-gray-800/90 px-5 text-white transition-colors hover:bg-gray-700"
              >
                查看牌桌
              </button>
              <button
                type="button"
                onClick={onReturnToHome}
                className="h-12 min-w-28 rounded-lg bg-orange-500 px-5 text-white transition-colors hover:bg-orange-400"
              >
                返回大厅
              </button>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {hidden && (
        <button
          type="button"
          onClick={() => setHidden(false)}
          className="fixed z-[95] h-12 min-w-28 rounded-lg border border-orange-300/60 bg-gray-950/90 px-4 text-sm font-bold text-orange-200 shadow-lg backdrop-blur-sm transition-colors hover:bg-gray-800"
          style={{
            right: "calc(1rem + var(--layout-safe-right, env(safe-area-inset-right)))",
            bottom: "calc(1rem + var(--layout-safe-bottom, env(safe-area-inset-bottom)))",
          }}
        >
          查看结算
        </button>
      )}
    </>
  );
}
