"use client";

import { useState } from "react";
import { useDeckStore } from "@/store/deckStore";
import CardItem from "@/components/ui/CardItem";
import type { CardData } from "@/types/card";

export default function SimulatePanel() {
  const { leader, entries } = useDeckStore();
  const [hand, setHand] = useState<CardData[]>([]);
  const [drawPile, setDrawPile] = useState<CardData[]>([]);

  const initSimulate = () => {
    const deck: CardData[] = entries.flatMap((e) =>
      Array(e.count).fill(e.card)
    );
    const shuffled = [...deck].sort(() => Math.random() - 0.5);
    setHand(shuffled.slice(0, 5));
    setDrawPile(shuffled.slice(5));
  };

  const draw = () => {
    if (drawPile.length === 0) return;
    setHand((h) => [...h, drawPile[0]]);
    setDrawPile((d) => d.slice(1));
  };

  const reset = () => {
    setHand([]);
    setDrawPile([]);
  };

  return (
    <div className="flex flex-col h-full p-4 gap-4">
      <h2 className="text-white font-bold text-sm">模拟抽卡</h2>

      <div className="flex gap-2">
        <button
          onClick={initSimulate}
          className="flex-1 py-2 text-xs text-white bg-blue-600 hover:bg-blue-500 rounded-lg transition-colors font-bold"
        >
          开始模拟
        </button>
        <button
          onClick={draw}
          disabled={drawPile.length === 0}
          className="flex-1 py-2 text-xs text-white bg-gray-700 hover:bg-gray-600 disabled:opacity-50 rounded-lg transition-colors"
        >
          抽一张
        </button>
        <button
          onClick={reset}
          className="py-2 px-3 text-xs text-red-400 bg-gray-800 hover:bg-gray-700 rounded-lg transition-colors"
        >
          重置
        </button>
      </div>

      {leader && (
        <div className="flex items-center gap-2">
          <span className="text-gray-400 text-xs">领航</span>
          <CardItem card={leader} size="sm" />
        </div>
      )}

      <div>
        <p className="text-gray-400 text-xs mb-2">
          手牌 ({hand.length}) · 剩余 {drawPile.length} 张
        </p>
        <div className="flex flex-wrap gap-2">
          {hand.map((card, i) => (
            <CardItem key={`${card.number}-${i}`} card={card} size="sm" />
          ))}
          {hand.length === 0 && (
            <p className="text-gray-600 text-xs">点击"开始模拟"洗牌抽手</p>
          )}
        </div>
      </div>
    </div>
  );
}
