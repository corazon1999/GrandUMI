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
      {currentTurn && selectedHandIndex !== null && (
        <button
          onClick={handlePlayCard}
          disabled={isPending}
          className="absolute bottom-20 right-3 rounded-md bg-blue-500 px-5 py-2 text-sm font-bold text-white shadow-lg transition-colors hover:bg-blue-400 disabled:cursor-not-allowed disabled:bg-gray-600"
        >
          出牌
        </button>
      )}

      {currentTurn && (
        <button
          onClick={endTurn}
          disabled={isPending}
          className="absolute bottom-3 right-3 rounded-md bg-orange-500 px-5 py-3 text-sm font-black text-white shadow-lg transition-colors hover:bg-orange-400 disabled:cursor-not-allowed disabled:bg-gray-600"
        >
          结束回合
        </button>
      )}

      <button
        onClick={() => setOpen(true)}
        disabled={isPending}
        className="absolute right-4 top-4 h-9 w-9 rounded-md bg-slate-800 text-lg leading-none text-slate-300 transition-colors hover:bg-slate-700 disabled:cursor-not-allowed disabled:bg-gray-600"
        aria-label="打开游戏菜单"
      >
        ≡
      </button>

      <Modal open={open} onClose={() => setOpen(false)} title="游戏菜单">
        <div className="flex flex-col gap-2">
          <button
            onClick={() => setOpen(false)}
            className="w-full rounded-lg bg-gray-700 py-2 text-sm text-white transition-colors hover:bg-gray-600"
          >
            继续游戏
          </button>
          <button
            onClick={handleSurrender}
            className="w-full rounded-lg py-2 text-sm text-red-400 transition-colors hover:bg-gray-700"
          >
            投降
          </button>
        </div>
      </Modal>
    </>
  );
}
