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

  if (!player) return <div className="min-h-20" />;

  const cards = side === "my"
    ? player.handCardNumbers.map((n) => getCard(n) ?? null)
    : Array.from({ length: player.handCount }, () => null);

  const handleClick = (i: number) => {
    if (hidden || isPending) return;
    if (side !== "my" || !currentTurn) return;
    setSelectedHand(selectedHandIndex === i ? null : i);
  };

  return (
    <div className="flex min-h-24 items-end justify-center gap-2 overflow-x-auto px-3 py-1 lg:min-h-32">
      <AnimatePresence>
        {cards.map((card, i) => (
          <motion.div
            key={`${card?.number ?? "back"}-${i}`}
            initial={{ y: side === "my" ? 36 : -24, opacity: 0 }}
            animate={{ y: 0, opacity: 1 }}
            exit={{ y: side === "my" ? -24 : 24, opacity: 0 }}
            transition={{ delay: i * 0.04, type: "spring", stiffness: 200 }}
            className={side === "my" ? "-mx-1 lg:-mx-1.5" : "-mx-2 lg:-mx-3"}
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
        <span className="text-xs text-gray-700">
          {hidden ? "对手手牌" : "手牌为空"}
        </span>
      )}
    </div>
  );
}
