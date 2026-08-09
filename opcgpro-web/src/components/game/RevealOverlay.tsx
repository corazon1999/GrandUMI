"use client";

import { useEffect, useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { useGameStore } from "@/store/gameStore";
import { getGameCard } from "@/data/CardLoader";
import CardItem from "@/components/ui/CardItem";
import type { CardData } from "@/types/card";

/** 公开浮层停留时长（毫秒） */
const REVEAL_DURATION = 2500;

type Shown = { cards: CardData[]; label: string };

/**
 * RevealOverlay — 检索/公开牌的被动展示浮层
 *
 * 当某方检索到牌加入手牌时，服务端在那一刻的快照里带上 reveal，
 * 此处居中放大显示约 2.5 秒后自动消失（不拦截操作）。
 *
 * 把要展示的牌拷进本地 state，使其在 clearReveal() 清空 store 后仍能播放淡出动画。
 */
export default function RevealOverlay() {
  const reveal = useGameStore((s) => s.reveal);
  const clearReveal = useGameStore((s) => s.clearReveal);
  const mySpriteMap = useGameStore((s) => s.my?.spriteMap);
  const opponentSpriteMap = useGameStore((s) => s.opponent?.spriteMap);
  const [shown, setShown] = useState<Shown | null>(null);

  useEffect(() => {
    if (!reveal) return;
    const spriteMap = reveal.side === "my" ? mySpriteMap : opponentSpriteMap;
    const cards = reveal.cardNumbers
      .map((n) => getGameCard(n, spriteMap))
      .filter((c): c is CardData => !!c);
    if (cards.length === 0) {
      clearReveal();
      return;
    }
    setShown({ cards, label: reveal.side === "my" ? "你公开了" : "对方公开了" });
    const t = setTimeout(() => {
      setShown(null);
      clearReveal();
    }, REVEAL_DURATION);
    return () => clearTimeout(t);
    // nonce 变化即重新触发一次展示
  }, [reveal?.nonce, reveal, clearReveal, mySpriteMap, opponentSpriteMap]);

  return (
    <AnimatePresence>
      {shown && (
        <motion.div
          className="fixed inset-0 z-40 flex flex-col items-center justify-center gap-4 pointer-events-none"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.2 }}
        >
          {/* 半透明压暗背景，弱于 Prompt 弹窗 */}
          <div className="absolute inset-0 bg-black/45" />

          <motion.div
            className="relative flex flex-col items-center gap-4"
            initial={{ scale: 0.7, y: 20 }}
            animate={{ scale: 1, y: 0 }}
            exit={{ scale: 0.85, opacity: 0 }}
            transition={{ type: "spring", stiffness: 220, damping: 22 }}
          >
            <div className="rounded-full bg-orange-500/90 px-5 py-1.5 text-base font-bold text-white shadow-lg">
              {shown.label}
            </div>
            <div className="flex flex-wrap justify-center gap-3">
              {shown.cards.map((card, i) => (
                <CardItem key={i} card={card} size="lg" />
              ))}
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
