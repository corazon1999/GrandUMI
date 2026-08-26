"use client";

import { type FormEvent, useEffect, useId, useRef, useState } from "react";
import { GameRequest } from "@/net/GameRequest";
import Modal from "@/components/ui/Modal";
import PlayerSafetyActions from "@/components/ui/PlayerSafetyActions";
import { useLayoutSettings } from "@/components/home/LayoutSettingsProvider";
import { useGameStore } from "@/store/gameStore";
import { showMessage } from "@/components/ui/MessageBox";
import {
  DRAW_REQUEST_DESCRIPTION_MAX_LENGTH,
  prepareDrawRequestDescription,
} from "@/lib/drawRequest";

function MoreIcon() {
  return (
    <svg
      viewBox="0 0 24 24"
      className="h-4 w-4"
      fill="currentColor"
      aria-hidden="true"
    >
      <circle cx="5" cy="12" r="1.75" />
      <circle cx="12" cy="12" r="1.75" />
      <circle cx="19" cy="12" r="1.75" />
    </svg>
  );
}

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onOpenFeedback: () => void;
  targetName: string;
  playerToolsEnabled?: boolean;
}

/**
 * 低频对局工具统一入口。核心操作仍留在 GameActions，
 * 这里只复用设置、反馈、平局/投降及玩家安全操作。
 */
export default function GameMenu({
  open,
  onOpenChange,
  onOpenFeedback,
  targetName,
  playerToolsEnabled = true,
}: Props) {
  const [drawRequestFormOpen, setDrawRequestFormOpen] = useState(false);
  const [drawDescription, setDrawDescription] = useState("");
  const [drawDescriptionError, setDrawDescriptionError] = useState<string | null>(null);
  const drawDescriptionId = useId();
  const { openSettings, suppressSettingsTrigger } = useLayoutSettings();
  const matchKind = useGameStore((state) => state.matchKind);
  const isPending = useGameStore((state) => state.isPending);
  const drawRequestPendingFromMe = useGameStore((state) => state.drawRequestPendingFromMe);
  const drawRequestPendingFromOpponent = useGameStore((state) => state.drawRequestPendingFromOpponent);
  const drawRequestDescription = useGameStore((state) => state.drawRequestDescription);
  const drawRequestRejectionCount = useGameStore((state) => state.drawRequestRejectionCount);
  const drawRequestRejectionLimit = useGameStore((state) => state.drawRequestRejectionLimit);
  const lastAction = useGameStore((state) => state.lastAction);
  const previousRejectionCount = useRef(drawRequestRejectionCount);

  // 对局中的设置只从“更多”进入；卸载时恢复大厅/回放页的独立设置齿轮。
  useEffect(() => suppressSettingsTrigger(), [suppressSettingsTrigger]);

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

  useEffect(() => {
    if (playerToolsEnabled && drawRequestPendingFromOpponent && open) onOpenChange(false);
  }, [drawRequestPendingFromOpponent, onOpenChange, open, playerToolsEnabled]);

  const handleSurrender = () => {
    onOpenChange(false);
    GameRequest.surrender();
  };

  const openDrawRequestForm = () => {
    onOpenChange(false);
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

  const menuButtonClass =
    "min-h-12 rounded-lg border border-white/10 bg-slate-800/80 px-3 py-2 text-sm font-bold text-white transition-colors hover:bg-slate-700 disabled:cursor-not-allowed disabled:bg-slate-900 disabled:text-gray-600";

  return (
    <PlayerSafetyActions
      targetName={targetName}
      currentOpponent
      renderActions={(safety) => (
        <>
          <button
            type="button"
            onClick={() => onOpenChange(!open)}
            disabled={playerToolsEnabled && (drawRequestFormOpen || drawRequestPendingFromOpponent)}
            data-game-more-trigger
            className="relative flex h-12 w-12 min-h-12 min-w-12 items-center justify-center rounded-full text-slate-200 transition-colors focus-visible:outline-2 focus-visible:outline-slate-200 disabled:cursor-not-allowed disabled:opacity-50"
            aria-label="打开更多对局工具"
            aria-expanded={open}
            title="更多"
          >
            <span className="flex h-9 w-9 items-center justify-center rounded-full bg-slate-800/85 shadow-lg ring-1 ring-white/15 transition-colors hover:bg-slate-700">
              <MoreIcon />
            </span>
          </button>

          <Modal
            open={open && (!playerToolsEnabled || !drawRequestPendingFromOpponent)}
            onClose={() => onOpenChange(false)}
            title="更多对局工具"
            maxWidthClass="max-w-md"
          >
            <div className="grid grid-cols-2 gap-2">
              <button
                type="button"
                onClick={() => {
                  onOpenChange(false);
                  openSettings();
                }}
                className={menuButtonClass}
              >
                设置
              </button>
              <button
                type="button"
                onClick={() => {
                  onOpenChange(false);
                  onOpenFeedback();
                }}
                className={menuButtonClass}
              >
                反馈 Bug / 建议
              </button>
              {playerToolsEnabled && (
                <>
                  <button
                    type="button"
                    onClick={openDrawRequestForm}
                    disabled={drawRequestDisabled}
                    className={`${menuButtonClass} text-amber-200`}
                  >
                    {matchKind === "Bot"
                      ? "机器人对局无法请求平局"
                      : drawRequestPendingFromMe
                        ? `等待对方回应（已拒绝 ${drawRequestRejectionCount}/${drawRequestRejectionLimit} 次）`
                        : `出bug了，请求平局（已拒绝 ${drawRequestRejectionCount}/${drawRequestRejectionLimit} 次）`}
                  </button>
                  <button
                    type="button"
                    onClick={handleSurrender}
                    className={`${menuButtonClass} text-red-300 hover:bg-red-950/60`}
                  >
                    投降
                  </button>
                  <button
                    type="button"
                    onClick={() => {
                      if (safety.block()) onOpenChange(false);
                    }}
                    onBlur={safety.cancelBlockConfirmation}
                    aria-label={safety.confirmBlock ? `确认屏蔽玩家 ${targetName}` : `屏蔽玩家 ${targetName}`}
                    className={`${menuButtonClass} ${safety.confirmBlock ? "border-red-400 bg-red-950 text-red-200" : "text-slate-300"}`}
                  >
                    {safety.confirmBlock ? "再次点击确认屏蔽" : "屏蔽对手"}
                  </button>
                  <button
                    type="button"
                    onClick={() => {
                      onOpenChange(false);
                      safety.openReport();
                    }}
                    className={`${menuButtonClass} text-amber-300`}
                  >
                    举报对手
                  </button>
                </>
              )}
            </div>
          </Modal>

          <Modal
            open={playerToolsEnabled && drawRequestFormOpen && !drawRequestPendingFromOpponent}
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

          <Modal open={playerToolsEnabled && drawRequestPendingFromOpponent} title="对方请求平局" maxWidthClass="max-w-sm">
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
      )}
    />
  );
}
