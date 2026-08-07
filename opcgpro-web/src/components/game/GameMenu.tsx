"use client";

import { useState } from "react";
import { useGameStore } from "@/store/gameStore";
import { GameRequest } from "@/net/GameRequest";
import Modal from "@/components/ui/Modal";

/**
 * 全局游戏菜单：右上角 ≡ 按钮，打开后可投降。
 * 上下文操作（攻击/出牌/结束反击/结束回合）已迁至 GameActions。
 */
export default function GameMenu() {
  const [open, setOpen] = useState(false);
  const isPending = useGameStore((s) => s.isPending);

  const handleSurrender = () => {
    setOpen(false);
    GameRequest.surrender();
  };

  return (
    <>
      <button
        onClick={() => setOpen(true)}
        disabled={isPending}
        className="absolute right-16 top-4 z-20 h-9 w-9 rounded-md bg-slate-800 text-lg leading-none text-slate-300 transition-colors hover:bg-slate-700 disabled:cursor-not-allowed disabled:bg-gray-600"
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
