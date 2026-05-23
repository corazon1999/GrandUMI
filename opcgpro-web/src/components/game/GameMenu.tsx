"use client";

import { useState } from "react";
import { useGameStore } from "@/store/gameStore";
import { useBattleStore } from "@/store/battleStore";
import { GameRequest } from "@/net/GameRequest";
import Modal from "@/components/ui/Modal";

export default function GameMenu() {
  const [open, setOpen] = useState(false);
  const currentTurn = useGameStore((s) => s.currentTurn);
  const isPending = useGameStore((s) => s.isPending);
  const selectedHandIndex = useGameStore((s) => s.selectedHandIndex);
  const endTurn = useBattleStore((s) => s.endTurn);

  const handleSurrender = () => {
    setOpen(false);
    GameRequest.surrender();
  };

  const handlePlayCard = () => {
    if (selectedHandIndex === null) return;
    GameRequest.playCard(selectedHandIndex);
  };

  return (
    <>
      {/* 出牌按钮：选中手牌且是我的回合时显示 */}
      {currentTurn && selectedHandIndex !== null && (
        <button
          onClick={handlePlayCard}
          disabled={isPending}
          className="absolute bottom-24 right-3 bg-blue-500 hover:bg-blue-400 disabled:bg-gray-600 disabled:cursor-not-allowed text-white text-sm font-bold px-4 py-2 rounded-xl transition-colors shadow-lg"
        >
          出牌
        </button>
      )}

      {/* 回合结束按钮 */}
      {currentTurn && (
        <button
          onClick={endTurn}
          disabled={isPending}
          className="absolute bottom-36 right-3 bg-orange-500 hover:bg-orange-400 disabled:bg-gray-600 disabled:cursor-not-allowed text-white text-sm font-bold px-4 py-2 rounded-xl transition-colors shadow-lg"
        >
          结束回合
        </button>
      )}

      {/* 菜单按钮 */}
      <button
        onClick={() => setOpen(true)}
        disabled={isPending}
        className="absolute top-3 right-3 w-8 h-8 bg-gray-800 hover:bg-gray-700 disabled:bg-gray-600 disabled:cursor-not-allowed text-gray-400 rounded-lg text-lg leading-none transition-colors"
      >
        ≡
      </button>

      <Modal open={open} onClose={() => setOpen(false)} title="游戏菜单">
        <div className="flex flex-col gap-2">
          <button
            onClick={() => setOpen(false)}
            className="w-full py-2 text-sm text-white bg-gray-700 hover:bg-gray-600 rounded-lg transition-colors"
          >
            继续游戏
          </button>
          <button
            onClick={handleSurrender}
            className="w-full py-2 text-sm text-red-400 hover:bg-gray-700 rounded-lg transition-colors"
          >
            投降
          </button>
        </div>
      </Modal>
    </>
  );
}
