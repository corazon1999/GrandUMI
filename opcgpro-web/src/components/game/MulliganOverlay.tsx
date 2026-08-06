"use client";

import { useEffect, useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { useGameStore } from "@/store/gameStore";
import { GameRequest } from "@/net/GameRequest";
import { getCard } from "@/data/CardLoader";
import CardItem from "@/components/ui/CardItem";
import { useResponsive } from "@/hooks/useResponsive";

export default function MulliganOverlay() {
  const my = useGameStore((s) => s.my);
  const opp = useGameStore((s) => s.opponent);
  const firstPlayerChosen = useGameStore((s) => s.firstPlayerChosen);
  const isFirst = useGameStore((s) => s.isFirstPlayer);
  const mulliganBothDone = useGameStore((s) => s.mulliganBothDone);
  const mulliganDeadlineUtc = useGameStore((s) => s.mulliganDeadlineUtc);
  const { cardSize } = useResponsive();
  const [now, setNow] = useState(() => Date.now());

  if (!my) return null;
  if (!firstPlayerChosen) return null;
  if (mulliganBothDone) return null;

  const myDone = my.mulliganDone;
  const oppDone = opp?.mulliganDone ?? false;
  const choosing = !myDone;
  const deadlineMs = mulliganDeadlineUtc ? Date.parse(mulliganDeadlineUtc) : Number.NaN;
  const remainingSeconds = Number.isFinite(deadlineMs)
    ? Math.max(0, Math.ceil((deadlineMs - now) / 1000))
    : 60;
  const isExpiring = remainingSeconds <= 10;
  const timedOut = remainingSeconds === 0;

  useEffect(() => {
    if (!mulliganDeadlineUtc || mulliganBothDone) return;
    setNow(Date.now());
    const timer = window.setInterval(() => setNow(Date.now()), 250);
    return () => window.clearInterval(timer);
  }, [mulliganDeadlineUtc, mulliganBothDone]);

  const handCards = my.handCardNumbers.map((n) => getCard(n) ?? null);

  return (
    <AnimatePresence>
      <motion.div
        className="fixed inset-0 z-50 bg-black/80 flex flex-col items-center justify-center gap-6"
        initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
      >
        <div className="text-center">
          <p className="text-white text-lg font-bold mb-1">{isFirst ? "你是先手" : "你是后手"}</p>
          <p className={`mb-2 text-sm font-black tabular-nums ${isExpiring ? "text-rose-300" : "text-amber-200"}`}>
            调度剩余 {remainingSeconds} 秒
          </p>
          <p className="text-gray-400 text-sm">
            {timedOut && choosing
              ? "时间到，正在自动保留手牌…"
              : choosing ? "是否要更换起始手牌？" : oppDone ? "进入对局..." : "等待对手选择..."}
          </p>
        </div>

        <div className="flex items-center justify-center gap-2 px-4">
          {handCards.map((card, i) => (
            <motion.div
              key={`mulligan-${card?.number ?? i}-${i}`}
              initial={{ y: 60, opacity: 0 }}
              animate={{ y: 0, opacity: 1 }}
              transition={{ delay: i * 0.08, type: "spring", stiffness: 200 }}
            >
              <CardItem card={card} size={cardSize === "sm" ? "md" : "lg"} />
            </motion.div>
          ))}
        </div>

        {choosing && (
          <motion.div className="flex gap-4"
            initial={{ y: 20, opacity: 0 }} animate={{ y: 0, opacity: 1 }} transition={{ delay: 0.4 }}>
            <button onClick={() => GameRequest.mulligan(true)} disabled={timedOut}
              className="bg-blue-600 hover:bg-blue-500 text-white px-8 py-3 rounded-lg font-bold text-base transition-colors disabled:cursor-wait disabled:opacity-50">
              更换
            </button>
            <button onClick={() => GameRequest.mulligan(false)} disabled={timedOut}
              className="bg-orange-500 hover:bg-orange-400 text-white px-8 py-3 rounded-lg font-bold text-base transition-colors disabled:cursor-wait disabled:opacity-50">
              保留
            </button>
          </motion.div>
        )}

        {!choosing && (
          <motion.div className="flex items-center gap-3"
            initial={{ opacity: 0 }} animate={{ opacity: 1 }}>
            <div className="w-5 h-5 border-2 border-white/40 border-t-white rounded-full animate-spin" />
            <span className="text-gray-300 text-sm">等待对手完成选择...</span>
          </motion.div>
        )}
      </motion.div>
    </AnimatePresence>
  );
}
