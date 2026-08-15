"use client";

import { useEffect, useRef, useState } from "react";
import { GameRequest } from "@/net/GameRequest";
import Modal from "@/components/ui/Modal";
import { useGameStore } from "@/store/gameStore";
import { showMessage } from "@/components/ui/MessageBox";

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
  const matchKind = useGameStore((state) => state.matchKind);
  const drawRequestPendingFromMe = useGameStore((state) => state.drawRequestPendingFromMe);
  const drawRequestPendingFromOpponent = useGameStore((state) => state.drawRequestPendingFromOpponent);
  const drawRequestRejectionCount = useGameStore((state) => state.drawRequestRejectionCount);
  const drawRequestRejectionLimit = useGameStore((state) => state.drawRequestRejectionLimit);
  const previousRejectionCount = useRef(drawRequestRejectionCount);

  useEffect(() => {
    if (drawRequestRejectionCount > previousRejectionCount.current) {
      const reachedLimit = drawRequestRejectionCount >= drawRequestRejectionLimit;
      showMessage(
        reachedLimit
          ? "对方不同意平局，本局已无法再次申请"
          : `对方不同意平局（已拒绝 ${drawRequestRejectionCount}/${drawRequestRejectionLimit} 次）`,
        "warn",
      );
    }
    previousRejectionCount.current = drawRequestRejectionCount;
  }, [drawRequestRejectionCount, drawRequestRejectionLimit]);

  const handleSurrender = () => {
    setOpen(false);
    GameRequest.surrender();
  };

  const handleRequestDraw = () => {
    setOpen(false);
    GameRequest.requestDraw();
  };

  const handleRespondDraw = (accept: boolean) => {
    GameRequest.respondDraw(accept);
  };

  const drawRequestDisabled = matchKind === "Bot"
    || drawRequestPendingFromMe
    || drawRequestRejectionCount >= drawRequestRejectionLimit;

  return (
    <>
      <button
        onClick={() => setOpen(true)}
        style={{
          right: "calc(4rem + var(--layout-safe-right, env(safe-area-inset-right)))",
          top: "calc(1rem + var(--layout-safe-top, env(safe-area-inset-top)))",
        }}
        className="fixed z-[70] flex h-12 w-12 items-center justify-center rounded-lg bg-slate-800 text-white transition-colors hover:bg-slate-700"
        aria-label="打开投降菜单"
        title="投降"
      >
        <SurrenderFlagIcon />
      </button>

      <Modal open={open && !drawRequestPendingFromOpponent} onClose={() => setOpen(false)} title="游戏菜单" maxWidthClass="max-w-sm">
        <div className="flex flex-col gap-2">
          <button
            onClick={() => setOpen(false)}
            className="min-h-12 w-full rounded-lg bg-gray-700 px-4 py-2 text-sm text-white transition-colors hover:bg-gray-600"
          >
            继续游戏
          </button>
          <button
            onClick={handleRequestDraw}
            disabled={drawRequestDisabled}
            className="min-h-12 w-full rounded-lg px-4 py-2 text-sm text-amber-300 transition-colors hover:bg-gray-700 disabled:cursor-not-allowed disabled:text-gray-600 disabled:hover:bg-transparent"
          >
            {matchKind === "Bot"
              ? "机器人对局无法请求平局"
              : drawRequestPendingFromMe
                ? `等待对方回应（已拒绝 ${drawRequestRejectionCount}/${drawRequestRejectionLimit} 次）`
                : `出bug了，请求平局（已拒绝 ${drawRequestRejectionCount}/${drawRequestRejectionLimit} 次）`}
          </button>
          <button
            onClick={handleSurrender}
            className="min-h-12 w-full rounded-lg px-4 py-2 text-sm text-red-400 transition-colors hover:bg-gray-700"
          >
            投降
          </button>
        </div>
      </Modal>

      <Modal open={drawRequestPendingFromOpponent} title="对方请求平局" maxWidthClass="max-w-sm">
        <div className="space-y-4">
          <p className="text-sm leading-6 text-gray-300">
            对方表示本局出现了 Bug，并请求以平局结束。平局不会改变双方赏金，也不会影响连胜或连败。
          </p>
          <div className="grid grid-cols-2 gap-3">
            <button
              onClick={() => handleRespondDraw(false)}
              className="min-h-12 rounded-lg bg-gray-700 px-4 py-2 text-sm font-bold text-white transition-colors hover:bg-gray-600"
            >
              不同意
            </button>
            <button
              onClick={() => handleRespondDraw(true)}
              className="min-h-12 rounded-lg bg-emerald-600 px-4 py-2 text-sm font-bold text-white transition-colors hover:bg-emerald-500"
            >
              同意平局
            </button>
          </div>
        </div>
      </Modal>
    </>
  );
}
