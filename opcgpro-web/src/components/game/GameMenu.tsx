"use client";

import { type FormEvent, useEffect, useId, useRef, useState } from "react";
import { GameRequest } from "@/net/GameRequest";
import Modal from "@/components/ui/Modal";
import { useGameStore } from "@/store/gameStore";
import { showMessage } from "@/components/ui/MessageBox";
import {
  DRAW_REQUEST_DESCRIPTION_MAX_LENGTH,
  prepareDrawRequestDescription,
} from "@/lib/drawRequest";

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
  const [drawRequestFormOpen, setDrawRequestFormOpen] = useState(false);
  const [drawDescription, setDrawDescription] = useState("");
  const [drawDescriptionError, setDrawDescriptionError] = useState<string | null>(null);
  const drawDescriptionId = useId();
  const matchKind = useGameStore((state) => state.matchKind);
  const isPending = useGameStore((state) => state.isPending);
  const drawRequestPendingFromMe = useGameStore((state) => state.drawRequestPendingFromMe);
  const drawRequestPendingFromOpponent = useGameStore((state) => state.drawRequestPendingFromOpponent);
  const drawRequestDescription = useGameStore((state) => state.drawRequestDescription);
  const drawRequestRejectionCount = useGameStore((state) => state.drawRequestRejectionCount);
  const drawRequestRejectionLimit = useGameStore((state) => state.drawRequestRejectionLimit);
  const lastAction = useGameStore((state) => state.lastAction);
  const previousRejectionCount = useRef(drawRequestRejectionCount);

  useEffect(() => {
    if (lastAction === "DrawRequestRejected"
      && drawRequestRejectionCount > previousRejectionCount.current) {
      const reachedLimit = drawRequestRejectionCount >= drawRequestRejectionLimit;
      showMessage(
        reachedLimit
          ? "对方不同意平局，本局已无法再次申请"
          : `对方不同意平局（已拒绝 ${drawRequestRejectionCount}/${drawRequestRejectionLimit} 次）`,
        "warn",
      );
    }
    previousRejectionCount.current = drawRequestRejectionCount;
  }, [drawRequestRejectionCount, drawRequestRejectionLimit, lastAction]);

  useEffect(() => {
    if (!drawRequestPendingFromMe) return;
    setDrawRequestFormOpen(false);
    setDrawDescription("");
    setDrawDescriptionError(null);
  }, [drawRequestPendingFromMe]);

  const handleSurrender = () => {
    setOpen(false);
    GameRequest.surrender();
  };

  const openDrawRequestForm = () => {
    setOpen(false);
    setDrawDescriptionError(null);
    setDrawRequestFormOpen(true);
  };

  const closeDrawRequestForm = () => {
    setDrawRequestFormOpen(false);
    setDrawDescription("");
    setDrawDescriptionError(null);
  };

  const handleRequestDraw = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (isPending) return;

    const prepared = prepareDrawRequestDescription(drawDescription);
    if (!prepared.ok) {
      setDrawDescriptionError(prepared.error);
      return;
    }

    setDrawDescriptionError(null);
    if (!GameRequest.requestDraw(prepared.description)) {
      setDrawDescriptionError("网络连接不可用，请稍后重试");
    }
  };

  const handleRespondDraw = (accept: boolean) => {
    if (isPending) return;
    GameRequest.respondDraw(accept);
  };

  const drawRequestDisabled = matchKind === "Bot"
    || isPending
    || drawRequestPendingFromMe
    || drawRequestRejectionCount >= drawRequestRejectionLimit;

  return (
    <>
      <button
        onClick={() => setOpen(true)}
        disabled={drawRequestFormOpen || drawRequestPendingFromOpponent}
        style={{
          right: "calc(4.125rem + var(--layout-safe-right, env(safe-area-inset-right)))",
          top: "calc(0.625rem + var(--layout-safe-top, env(safe-area-inset-top)))",
        }}
        className="fixed z-[70] flex h-12 w-12 items-center justify-center rounded-lg border border-gray-700/80 bg-slate-800/95 text-white shadow-lg backdrop-blur-md transition-colors hover:bg-slate-700 disabled:cursor-not-allowed disabled:opacity-50"
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
            onClick={openDrawRequestForm}
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

      <Modal
        open={drawRequestFormOpen && !drawRequestPendingFromOpponent}
        onClose={closeDrawRequestForm}
        title="请求因 Bug 平局"
        maxWidthClass="max-w-md"
      >
        <form className="space-y-3" onSubmit={handleRequestDraw}>
          <p className="text-sm leading-5 text-gray-300">
            请简要说明本局发生了什么 Bug。描述会随平局申请发送给对方，由对方决定是否同意。
          </p>
          <div>
            <label htmlFor={drawDescriptionId} className="mb-1 block text-sm font-bold text-amber-200">
              发生了什么 Bug？
            </label>
            <textarea
              id={drawDescriptionId}
              value={drawDescription}
              onChange={(event) => {
                setDrawDescription(event.target.value);
                if (drawDescriptionError) setDrawDescriptionError(null);
              }}
              maxLength={DRAW_REQUEST_DESCRIPTION_MAX_LENGTH}
              rows={4}
              autoFocus
              aria-describedby={`${drawDescriptionId}-hint ${drawDescriptionId}-error`}
              aria-invalid={drawDescriptionError ? "true" : "false"}
              className="h-24 w-full resize-none rounded-lg border border-gray-600 bg-slate-950/80 px-3 py-2 text-sm leading-5 text-white outline-none transition-colors placeholder:text-gray-500 focus:border-amber-400 focus:ring-2 focus:ring-amber-400/20"
              placeholder="例如：对方发动效果后，我无法继续选择卡牌……"
            />
            <div className="mt-1 flex min-h-5 items-start justify-between gap-3 text-xs">
              <span
                id={`${drawDescriptionId}-error`}
                role={drawDescriptionError ? "alert" : undefined}
                className="text-rose-300"
              >
                {drawDescriptionError ?? ""}
              </span>
              <span id={`${drawDescriptionId}-hint`} className="shrink-0 text-gray-400">
                {drawDescription.length}/{DRAW_REQUEST_DESCRIPTION_MAX_LENGTH}
              </span>
            </div>
          </div>
          <div className="grid grid-cols-2 gap-3">
            <button
              type="button"
              onClick={closeDrawRequestForm}
              disabled={isPending}
              className="min-h-[52px] rounded-lg bg-gray-700 px-4 py-2 text-sm font-bold text-white transition-colors hover:bg-gray-600 disabled:cursor-not-allowed disabled:opacity-50"
            >
              取消
            </button>
            <button
              type="submit"
              disabled={isPending}
              className="min-h-[52px] rounded-lg bg-amber-500 px-4 py-2 text-sm font-bold text-slate-950 transition-colors hover:bg-amber-400 disabled:cursor-not-allowed disabled:opacity-50"
            >
              发送申请
            </button>
          </div>
        </form>
      </Modal>

      <Modal open={drawRequestPendingFromOpponent} title="对方请求平局" maxWidthClass="max-w-sm">
        <div className="space-y-3">
          <p className="text-sm leading-5 text-gray-300">
            对方表示本局出现了 Bug，并请求以平局结束。平局不会改变双方赏金，也不会影响连胜或连败。
          </p>
          <div>
            <p className="mb-1 text-xs font-bold text-amber-200">对方填写的 Bug 描述</p>
            <blockquote
              aria-label="对方填写的 Bug 描述"
              className="max-h-28 overflow-y-auto whitespace-pre-wrap break-words rounded-lg border border-amber-300/25 bg-amber-950/25 p-3 text-sm leading-5 text-amber-50"
            >
              {drawRequestDescription || "未收到 Bug 描述，请谨慎处理。"}
            </blockquote>
          </div>
          <div className="grid grid-cols-2 gap-3">
            <button
              onClick={() => handleRespondDraw(false)}
              disabled={isPending}
              className="min-h-[52px] rounded-lg bg-gray-700 px-4 py-2 text-sm font-bold text-white transition-colors hover:bg-gray-600 disabled:cursor-not-allowed disabled:opacity-50"
            >
              不同意
            </button>
            <button
              onClick={() => handleRespondDraw(true)}
              disabled={isPending}
              className="min-h-[52px] rounded-lg bg-emerald-600 px-4 py-2 text-sm font-bold text-white transition-colors hover:bg-emerald-500 disabled:cursor-not-allowed disabled:opacity-50"
            >
              同意平局
            </button>
          </div>
        </div>
      </Modal>
    </>
  );
}
