"use client";

import { motion, AnimatePresence } from "framer-motion";
import { useGameStore } from "@/store/gameStore";
import { useResponsive } from "@/hooks/useResponsive";
import CardItem from "@/components/ui/CardItem";
import { getCard } from "@/data/CardLoader";

interface Props {
  side: "my" | "opponent";
  hidden?: boolean;
}

export default function HandArea({ side, hidden = false }: Props) {
  const player = useGameStore((s) => (side === "my" ? s.my : s.opponent));
  const currentTurn = useGameStore((s) => s.currentTurn);
  const isPending = useGameStore((s) => s.isPending);
  const selectedHandIndex = useGameStore((s) => s.selectedHandIndex);
  const setSelectedHand = useGameStore((s) => s.setSelectedHand);
  const { cardSize } = useResponsive();

  if (!player) return <div className="min-h-24" />;

  // 己方：用卡号查 CardData；对手：仅占位牌背
  const cards = side === "my"
    ? player.handCardNumbers.map((n) => getCard(n) ?? null)
    : Array.from({ length: player.handCount }, () => null);

  const handleClick = (i: number) => {
    if (hidden || isPending) return;
    if (side !== "my" || !currentTurn) return;
    setSelectedHand(selectedHandIndex === i ? null : i);
  };

  return (
    <div className="flex items-end justify-center gap-1 px-2 py-1 min-h-24 overflow-x-auto">
      <AnimatePresence>
        {cards.map((card, i) => (
          <motion.div
            key={`${card?.number ?? "back"}-${i}`}
            initial={{ y: 50, opacity: 0 }}
            animate={{ y: 0, opacity: 1 }}
            exit={{ y: -30, opacity: 0 }}
            transition={{ delay: i * 0.04, type: "spring", stiffness: 200 }}
          >
            <CardItem
              card={card}
              isSelected={!hidden && selectedHandIndex === i}
              faceDown={hidden || card === null}
              onClick={() => handleClick(i)}
              size={cardSize}
            />
          </motion.div>
        ))}
      </AnimatePresence>

      {cards.length === 0 && (
        <span className="text-gray-700 text-xs">
          {hidden ? "对手手牌" : "手牌为空"}
        </span>
      )}
    </div>
  );
}
