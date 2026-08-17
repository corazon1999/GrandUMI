"use client";

import { useEffect, useRef, useState } from "react";
import FriendsPanel from "@/components/home/FriendsPanel";
import { eventBus } from "@/net/eventBus";
import { GameRequest } from "@/net/GameRequest";
import { HomeRequest } from "@/net/HomeProtocol";
import { useNetStore } from "@/store/netStore";
import { useGameStore } from "@/store/gameStore";
import { useLayoutQuarterTurn } from "@/components/ui/ResponsiveScope";

/** 左下角局内聊天与独立好友中心入口。 */

const PRESETS = [
  "你好",
  "好牌！",
  "谢谢",
  "手下留情",
  "该你了",
  "认输吧",
  "GG",
  "网络卡了，稍等",
  "老板来了，等我一会",
];
const COOLDOWN_MS = 1300;

interface GameChatItem {
  id: number;
  text: string;
  fromName: string;
  isSelf: boolean;
  fromRole: "player" | "spectator";
}

interface ChatToast {
  text: string;
  fromName: string;
}

export default function GameChatPanel({
  isPlayback,
  isObserver,
}: {
  isPlayback: boolean;
  isObserver: boolean;
}) {
  const rotateQuarterTurn = useLayoutQuarterTurn();
  const myAccount = useNetStore((s) => s.account);
  const friendChatUnreadByAccount = useNetStore(
    (s) => s.friendChatUnreadByAccount,
  );
  const incomingFriendCount = useNetStore(
    (s) => s.incomingFriendRequests.length,
  );
  const matchKind = useGameStore((s) => s.matchKind);
  const spectatorNames = useGameStore((s) => s.spectatorNames);
  const spectatorDetails = useGameStore((s) => s.spectatorDetails);
  const spectatorHandRequests = useGameStore((s) => s.spectatorHandRequests);
  const spectatorHandVisible = useGameStore((s) => s.spectatorHandVisible);
  const observerHandRequestStatus = useGameStore(
    (s) => s.observerHandRequestStatus,
  );
  const observerHandRequestRetryAt = useGameStore(
    (s) => s.observerHandRequestRetryAt,
  );
  const [open, setOpen] = useState(false);
  const [friendsOpen, setFriendsOpen] = useState(false);
  const [muted, setMuted] = useState(false);
  const [gameInput, setGameInput] = useState("");
  const [gameMessages, setGameMessages] = useState<GameChatItem[]>([]);
  const [coolingDown, setCoolingDown] = useState(false);
  const [gameUnread, setGameUnread] = useState(0);
  const [spectatorHovered, setSpectatorHovered] = useState(false);
  const [spectatorPinned, setSpectatorPinned] = useState(false);
  const [toast, setToast] = useState<ChatToast | null>(null);
  const [cooldownSeconds, setCooldownSeconds] = useState(0);
  const [kickConfirm, setKickConfirm] = useState("");
  const [opponentFriendRequestSent, setOpponentFriendRequestSent] =
    useState(false);

  const idRef = useRef(0);
  const mutedRef = useRef(muted);
  const accountRef = useRef(myAccount);
  const openRef = useRef(open);
  const listRef = useRef<HTMLDivElement>(null);
  const toastTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const cooldownTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const opponentFriendRequestTimer =
    useRef<ReturnType<typeof setTimeout> | null>(null);

  const totalFriendUnread = Object.values(friendChatUnreadByAccount).reduce(
    (total, count) => total + count,
    0,
  );
  const friendAlertCount = totalFriendUnread + incomingFriendCount;

  useEffect(() => {
    mutedRef.current = muted;
  }, [muted]);
  useEffect(() => {
    accountRef.current = myAccount;
  }, [myAccount]);
  useEffect(() => {
    openRef.current = open;
  }, [open]);
  useEffect(() => {
    if (spectatorNames.length === 0) setSpectatorPinned(false);
  }, [spectatorNames.length]);

  useEffect(() => {
    if (observerHandRequestStatus !== "cooldown") {
      setCooldownSeconds(0);
      return;
    }
    const update = () => {
      const remaining = Math.max(
        0,
        Math.ceil((observerHandRequestRetryAt - Date.now()) / 1000),
      );
      setCooldownSeconds(remaining);
      if (remaining === 0)
        useGameStore.getState().setObserverHandRequestStatus("idle");
    };
    update();
    const timer = window.setInterval(update, 1000);
    return () => window.clearInterval(timer);
  }, [observerHandRequestRetryAt, observerHandRequestStatus]);

  const showToast = (nextToast: ChatToast) => {
    setToast(nextToast);
    if (toastTimer.current) clearTimeout(toastTimer.current);
    toastTimer.current = setTimeout(() => setToast(null), 4000);
  };

  useEffect(() => {
    const handler = (message: {
      text: string;
      fromAccount?: string;
      fromName: string;
      fromRole: "player" | "spectator";
    }) => {
      const isSelf =
        !!message.fromAccount && message.fromAccount === accountRef.current;
      if (mutedRef.current && !isSelf) return;
      const item: GameChatItem = {
        id: ++idRef.current,
        text: message.text,
        fromName: message.fromName,
        isSelf,
        fromRole: message.fromRole,
      };
      setGameMessages((previous) => [...previous.slice(-49), item]);
      if (!isSelf && !openRef.current) {
        setGameUnread((count) => count + 1);
        showToast({ text: item.text, fromName: item.fromName });
      }
    };
    eventBus.on("gameChat", handler);
    return () => {
      eventBus.off("gameChat", handler);
      if (toastTimer.current) clearTimeout(toastTimer.current);
      if (cooldownTimer.current) clearTimeout(cooldownTimer.current);
      if (opponentFriendRequestTimer.current)
        clearTimeout(opponentFriendRequestTimer.current);
    };
  }, []);

  useEffect(() => {
    if (!open || !listRef.current) return;
    listRef.current.scrollTop = listRef.current.scrollHeight;
  }, [gameMessages, open]);

  if (isPlayback) return null;

  const showSpectatorIndicator = !isObserver && spectatorNames.length > 0;
  const showSpectatorList =
    showSpectatorIndicator && (spectatorHovered || spectatorPinned);

  const fireCooldown = () => {
    setCoolingDown(true);
    if (cooldownTimer.current) clearTimeout(cooldownTimer.current);
    cooldownTimer.current = setTimeout(
      () => setCoolingDown(false),
      COOLDOWN_MS,
    );
  };

  const sendPreset = (text: string) => {
    if (coolingDown) return;
    GameRequest.sendGameChat(text, "preset");
    fireCooldown();
  };

  const sendGameMessage = () => {
    const text = gameInput.trim();
    if (!text || coolingDown) return;
    GameRequest.sendGameChat(text);
    setGameInput("");
    fireCooldown();
  };

  const requestHand = () => {
    if (observerHandRequestStatus !== "idle") return;
    if (GameRequest.requestSpectatorHand()) {
      useGameStore.getState().setObserverHandRequestStatus("pending");
    }
  };

  const respondHandRequest = (requestId: string, accept: boolean) => {
    GameRequest.respondSpectatorHand(requestId, accept);
    useGameStore.getState().removeSpectatorHandRequest(requestId);
  };

  const kickSpectator = (account: string) => {
    if (kickConfirm !== account) {
      setKickConfirm(account);
      return;
    }
    GameRequest.kickSpectator(account);
    setKickConfirm("");
  };

  const handleOpponentFriendAction = () => {
    if (opponentFriendRequestSent) return;
    if (HomeRequest.sendOpponentFriendRequest()) {
      setOpponentFriendRequestSent(true);
      if (opponentFriendRequestTimer.current)
        clearTimeout(opponentFriendRequestTimer.current);
      opponentFriendRequestTimer.current = setTimeout(
        () => setOpponentFriendRequestSent(false),
        2500,
      );
    }
  };

  return (
    <>
      <div
        data-game-chat-root
        data-layout-rotated={rotateQuarterTurn ? "true" : "false"}
        className="pointer-events-none fixed z-50"
        style={{
          ...(rotateQuarterTurn
            ? {
                right:
                  "calc(0.75rem + var(--layout-safe-right, env(safe-area-inset-right)))",
              }
            : {
                left: "calc(0.75rem + var(--layout-safe-left, env(safe-area-inset-left)))",
              }),
          bottom:
            "calc(0.75rem + var(--layout-safe-bottom, env(safe-area-inset-bottom)))",
        }}
      >
        <div
          data-game-chat-popovers
          className={`absolute flex flex-col gap-2 ${
            rotateQuarterTurn
              ? "bottom-0 right-[calc(100%+0.5rem)] items-end"
              : "bottom-[calc(100%+0.5rem)] left-0 items-start"
          }`}
        >
        {!isObserver &&
          spectatorHandRequests.map((request) => (
            <div
              key={request.requestId}
              className="pointer-events-auto w-72 max-w-[calc(100cqw-1.5rem-var(--layout-safe-left,0px)-var(--layout-safe-right,0px))] rounded-xl border border-purple-400/30 bg-slate-900/95 p-3 text-xs text-white shadow-2xl"
            >
              <p className="font-bold text-purple-200">
                {request.spectatorName} 申请查看你的手牌
              </p>
              <p className="mt-1 text-slate-400">
                只会向这名观战者公开你当前及后续手牌。
              </p>
              <div className="mt-3 grid grid-cols-2 gap-2">
                <button
                  type="button"
                  onClick={() => respondHandRequest(request.requestId, false)}
                  className="min-h-12 rounded-lg bg-slate-700 font-bold text-slate-200 hover:bg-slate-600"
                >
                  拒绝
                </button>
                <button
                  type="button"
                  onClick={() => respondHandRequest(request.requestId, true)}
                  className="min-h-12 rounded-lg bg-emerald-700 font-bold text-white hover:bg-emerald-600"
                >
                  同意公开
                </button>
              </div>
            </div>
          ))}
        {!open && toast && (
          <div className="pointer-events-none max-w-[240px] rounded-lg bg-black/80 px-3 py-1.5 text-xs text-white shadow-lg ring-1 ring-white/15">
            <span className="font-bold text-amber-300">{toast.fromName}：</span>
            {toast.text}
          </div>
        )}

        {open && (
          <div
            data-game-chat-dialog
            className={`pointer-events-auto flex flex-col overflow-hidden rounded-xl bg-slate-900/95 shadow-2xl ring-1 ring-white/15 ${
              rotateQuarterTurn ? "" : "w-80 max-md:w-64"
            }`}
            style={
              rotateQuarterTurn
                ? {
                    width:
                      "min(20rem, calc(100cqw - 5.25rem - var(--layout-safe-left, 0px) - var(--layout-safe-right, 0px)))",
                    height:
                      "min(22rem, calc(100cqh - 1.5rem - var(--layout-safe-top, 0px) - var(--layout-safe-bottom, 0px)))",
                  }
                : undefined
            }
          >
            <div className="flex items-center justify-between border-b border-white/10 px-3 py-1.5">
              <span className="text-xs font-bold text-slate-200">局内聊天</span>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => setMuted((value) => !value)}
                  className={`min-h-12 px-1 text-xs ${muted ? "text-rose-400" : "text-slate-400 hover:text-slate-200"}`}
                  title={
                    muted
                      ? "已静音局内其他玩家（点击取消）"
                      : "静音局内其他玩家"
                  }
                >
                  {muted ? "🔇 已静音" : "🔊"}
                </button>
                <button
                  type="button"
                  onClick={() => setOpen(false)}
                  aria-label="收起局内聊天"
                  className="flex h-12 w-12 items-center justify-center rounded-md text-slate-400 hover:bg-white/5 hover:text-white"
                  title="收起"
                >
                  ✕
                </button>
              </div>
            </div>

            <div
              ref={listRef}
              data-game-chat-message-list
              className={`${rotateQuarterTurn ? "min-h-0 flex-1" : "h-40"} overflow-y-auto overscroll-contain px-3 py-2 text-xs`}
            >
              {gameMessages.length === 0 && (
                <div className="text-slate-500">还没有消息。发个招呼吧～</div>
              )}
              {gameMessages.map((message) => (
                <div key={message.id} className="mb-1 leading-snug">
                  <span
                    className={`font-bold ${message.isSelf ? "text-sky-300" : message.fromRole === "spectator" ? "text-slate-400" : "text-amber-300"}`}
                  >
                    {message.isSelf ? "你" : message.fromName}
                    {message.fromRole === "spectator" ? "(观战)" : ""}：
                  </span>
                  <span className="text-slate-100">{message.text}</span>
                </div>
              ))}
            </div>
            <div
              data-game-chat-presets
              className={`gap-1 border-t border-white/10 px-2 py-1.5 ${
                rotateQuarterTurn
                  ? "flex shrink-0 touch-pan-x overflow-x-auto overscroll-x-contain"
                  : "flex flex-wrap"
              }`}
            >
              {PRESETS.map((preset) => (
                <button
                  key={preset}
                  type="button"
                  onClick={() => sendPreset(preset)}
                  disabled={coolingDown}
                  className="min-h-12 min-w-12 rounded-full shrink-0 bg-slate-700/80 px-2 py-0.5 text-[11px] text-slate-100 hover:bg-slate-600 disabled:opacity-40"
                >
                  {preset}
                </button>
              ))}
            </div>
            <ChatInput
              value={gameInput}
              onChange={setGameInput}
              onSend={sendGameMessage}
              disabled={coolingDown}
              placeholder="输入局内消息…"
            />
          </div>
        )}
        </div>

        <div
          className={`pointer-events-auto flex gap-2 ${rotateQuarterTurn ? "flex-col items-end" : "items-center"}`}
        >
          <button
            type="button"
            onClick={() => {
              setSpectatorPinned(false);
              setFriendsOpen(false);
              setOpen((value) => !value);
              if (!open) setGameUnread(0);
            }}
            className="relative flex h-12 w-12 items-center justify-center rounded-full bg-slate-800/90 text-lg shadow-lg ring-1 ring-white/15 hover:bg-slate-700"
            title="局内聊天"
            aria-label="打开局内聊天"
          >
            💬
            {!open && gameUnread > 0 && (
              <span className="absolute -right-1 -top-1 flex h-5 min-w-5 items-center justify-center rounded-full bg-rose-500 px-1 text-[10px] font-bold text-white ring-2 ring-slate-900">
                {gameUnread > 9 ? "9+" : gameUnread}
              </span>
            )}
          </button>

          {!isObserver && matchKind !== "Bot" && (
            <button
              type="button"
              onClick={handleOpponentFriendAction}
              disabled={opponentFriendRequestSent}
              data-opponent-friend-action
              className="flex h-12 w-12 items-center justify-center rounded-full bg-sky-800/95 text-sky-50 shadow-lg ring-1 ring-sky-300/30 transition-colors hover:bg-sky-700 disabled:cursor-wait disabled:bg-emerald-900/90 disabled:text-emerald-200"
              title={
                opponentFriendRequestSent
                  ? "好友申请已发送"
                  : "添加交战对手为好友"
              }
              aria-label={
                opponentFriendRequestSent
                  ? "好友申请已发送"
                  : "添加交战对手为好友"
              }
            >
              <svg
                viewBox="0 0 24 24"
                className="h-5 w-5"
                fill="none"
                stroke="currentColor"
                strokeWidth="1.8"
                strokeLinecap="round"
                strokeLinejoin="round"
                aria-hidden="true"
              >
                <circle cx="8.5" cy="8" r="3" />
                <path d="M3.5 19c.6-3.5 2.3-5 5-5 1.7 0 3 .5 3.9 1.5" />
                {opponentFriendRequestSent ? (
                  <path d="m14.5 17 2 2 4-5" />
                ) : (
                  <path d="M17.5 14v6M14.5 17h6" />
                )}
              </svg>
            </button>
          )}

          <button
            type="button"
            onClick={() => {
              setOpen(false);
              setSpectatorPinned(false);
              setFriendsOpen(true);
            }}
            className="relative flex h-12 w-12 items-center justify-center rounded-full bg-emerald-800/90 text-emerald-50 shadow-lg ring-1 ring-emerald-300/25 transition-colors hover:bg-emerald-700"
            title="好友中心"
            aria-label={`打开好友中心${totalFriendUnread ? `，${totalFriendUnread} 条好友消息` : ""}${incomingFriendCount ? `，${incomingFriendCount} 条新申请` : ""}`}
          >
            <svg
              viewBox="0 0 24 24"
              className="h-5 w-5"
              fill="none"
              stroke="currentColor"
              strokeWidth="1.8"
              strokeLinecap="round"
              strokeLinejoin="round"
              aria-hidden="true"
            >
              <circle cx="9" cy="8" r="3" />
              <path d="M3.5 19c.6-3.5 2.4-5 5.5-5s4.9 1.5 5.5 5M16 8h5M18.5 5.5v5" />
            </svg>
            {friendAlertCount > 0 && (
              <span className="absolute -right-1 -top-1 flex h-5 min-w-5 items-center justify-center rounded-full bg-rose-500 px-1 text-[10px] font-bold text-white ring-2 ring-slate-900">
                {friendAlertCount > 9 ? "9+" : friendAlertCount}
              </span>
            )}
          </button>

          {showSpectatorIndicator && (
            <div
              className="relative"
              onMouseEnter={() => setSpectatorHovered(true)}
              onMouseLeave={() => setSpectatorHovered(false)}
            >
              {showSpectatorList && (
                <div className="absolute bottom-full left-0 mb-2 w-52 rounded-xl bg-slate-900/95 p-3 text-xs text-white shadow-2xl ring-1 ring-purple-300/25">
                  <p className="mb-2 font-bold text-purple-200">
                    {spectatorNames.length} 人正在观战
                  </p>
                  <div className="max-h-40 space-y-1 overflow-y-auto">
                    {(spectatorDetails.length > 0
                      ? spectatorDetails
                      : spectatorNames.map((name) => ({
                          account: name,
                          name,
                          viewingYou: false,
                          handVisible: false,
                        }))
                    ).map((spectator, index) => (
                      <div
                        key={`${spectator.account}:${index}`}
                        className="flex min-h-12 items-center gap-2 rounded-md bg-white/5 px-2 py-1 text-slate-200"
                      >
                        <div className="min-w-0 flex-1">
                          <p className="truncate">{spectator.name}</p>
                          {spectator.viewingYou && (
                            <p className="text-[10px] text-purple-300">
                              主视角：你
                              {spectator.handVisible ? " · 已公开手牌" : ""}
                            </p>
                          )}
                        </div>
                        {spectatorDetails.length > 0 && (
                          <button
                            type="button"
                            onClick={() => kickSpectator(spectator.account)}
                            className={`min-h-12 rounded-md px-2 text-[10px] font-bold ${kickConfirm === spectator.account ? "bg-red-700 text-white" : "bg-slate-700 text-slate-300 hover:bg-red-900 hover:text-red-200"}`}
                          >
                            {kickConfirm === spectator.account
                              ? "确认踢出"
                              : "踢出"}
                          </button>
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              )}
              <button
                type="button"
                onClick={() => {
                  setOpen(false);
                  setSpectatorPinned((value) => !value);
                }}
                className="relative flex h-12 w-12 items-center justify-center rounded-full bg-purple-900/90 text-purple-100 shadow-lg ring-1 ring-purple-300/30 transition-colors hover:bg-purple-800"
                title={`${spectatorNames.length} 人正在观战`}
                aria-label={`查看正在观战的 ${spectatorNames.length} 人`}
                aria-expanded={showSpectatorList}
              >
                <svg
                  viewBox="0 0 24 24"
                  aria-hidden="true"
                  className="h-5 w-5 fill-none stroke-current stroke-2"
                >
                  <path d="M2.5 12s3.5-6 9.5-6 9.5 6 9.5 6-3.5 6-9.5 6-9.5-6-9.5-6Z" />
                  <circle cx="12" cy="12" r="2.75" />
                </svg>
                <span className="absolute -right-1 -top-1 flex h-5 min-w-5 items-center justify-center rounded-full bg-purple-500 px-1 text-[10px] font-bold text-white ring-2 ring-slate-900">
                  {spectatorNames.length > 99 ? "99+" : spectatorNames.length}
                </span>
              </button>
            </div>
          )}
          {isObserver && !spectatorHandVisible && (
            <button
              type="button"
              onClick={requestHand}
              disabled={observerHandRequestStatus !== "idle"}
              className="min-h-12 rounded-full bg-purple-900/90 px-4 text-xs font-bold text-purple-100 shadow-lg ring-1 ring-purple-300/30 transition-colors hover:bg-purple-800 disabled:cursor-not-allowed disabled:bg-slate-800 disabled:text-slate-500"
            >
              {observerHandRequestStatus === "pending"
                ? "等待玩家同意…"
                : observerHandRequestStatus === "cooldown"
                  ? `${cooldownSeconds || 1} 秒后可再申请`
                  : "申请查看主视角手牌"}
            </button>
          )}
        </div>
      </div>
      <FriendsPanel open={friendsOpen} onClose={() => setFriendsOpen(false)} />
    </>
  );
}

function ChatInput({
  value,
  onChange,
  onSend,
  disabled,
  placeholder,
}: {
  value: string;
  onChange: (value: string) => void;
  onSend: () => void;
  disabled: boolean;
  placeholder: string;
}) {
  return (
    <div className="flex items-center gap-1 border-t border-white/10 p-2">
      <input
        value={value}
        onChange={(event) => onChange(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === "Enter") onSend();
        }}
        disabled={disabled}
        maxLength={100}
        placeholder={placeholder}
        className="min-h-12 min-w-0 flex-1 rounded-md bg-slate-800 px-2 py-1.5 text-xs text-white outline-none ring-1 ring-white/10 placeholder:text-slate-500 focus:ring-sky-400 disabled:cursor-not-allowed disabled:opacity-60"
      />
      <button
        type="button"
        onClick={onSend}
        disabled={disabled || !value.trim()}
        className="min-h-12 rounded-md bg-sky-600 px-3 py-1.5 text-xs font-bold text-white hover:bg-sky-500 disabled:bg-slate-700 disabled:opacity-50"
      >
        发送
      </button>
    </div>
  );
}
