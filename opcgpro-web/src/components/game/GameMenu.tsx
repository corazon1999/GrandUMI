"use client";

import { useState } from "react";
import { GameRequest } from "@/net/GameRequest";
import Modal from "@/components/ui/Modal";

function SurrenderFlagIcon() {
  return (
    <svg
      viewBox="0 0 24 24"
      className="h-5 w-5"
      fill="none"
      aria-hidden="true"
    >
      <path d="M6.5 21V4" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
      <path
        d="M7 4.5c3-2 6 2 10.5 0v9c-4.5 2-7.5-2-10.5 0v-9Z"
        fill="currentColor"
      />
    </svg>
  );
}

/**
 * 全局游戏菜单：右上角白旗按钮，打开后可投降。
 * 上下文操作（攻击/出牌/结束反击/结束回合）已迁至 GameActions。
 */
export default function GameMenu() {
  const [open, setOpen] = useState(false);

  const handleSurrender = () => {
    setOpen(false);
    GameRequest.surrender();
  };

  return (
    <>
      <button
        onClick={() => setOpen(true)}
        className="fixed right-16 top-4 z-[70] flex h-9 w-9 items-center justify-center rounded-md bg-slate-800 text-white transition-colors hover:bg-slate-700"
        aria-label="打开投降菜单"
        title="投降"
      >
        <SurrenderFlagIcon />
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
