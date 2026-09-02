"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import FriendsPanel from "@/components/home/FriendsPanel";
import { eventBus } from "@/net/eventBus";
import { GameRequest } from "@/net/GameRequest";
import { useNetStore } from "@/store/netStore";
import { useGameStore } from "@/store/gameStore";
import { useLayoutQuarterTurn } from "@/components/ui/ResponsiveScope";
import SpectatorArena from "@/components/game/SpectatorArena";
import GameMenu from "@/components/game/GameMenu";
import MobileTurnExtensionButton from "@/components/game/MobileTurnExtensionButton";
import { useLanguage } from "@/i18n/LanguageProvider";
import type {
  ChatDecorationItem,
  ChatDecorationSlot,
} from "@/store/netStore";

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
const DECORATION_BUBBLE_MS = 4200;

const DECORATION_SLOTS: Array<{
  slot: ChatDecorationSlot;
  label: string;
  icon: string;
}> = [
  { slot: "greeting", label: "问候", icon: "👋" },
  { slot: "praise", label: "称赞", icon: "✨" },
  { slot: "thanks", label: "感谢", icon: "🤝" },
  { slot: "surprise", label: "惊叹", icon: "❗" },
  { slot: "mistake", label: "失误", icon: "🧭" },
  { slot: "threat", label: "威胁", icon: "⚔️" },
];

interface GameChatItem {
  id: number;
  text: string;
  fromName: string;
  isSelf: boolean;
  fromRole: "player" | "spectator";
  decoration?: {
    id: string;
    styleToken: string;
  } | null;
}

interface ChatToast {
  text: string;
  fromName: string;
  fromRole: "player" | "spectator";
}

interface DecorationBubble {
  id: number;
  text: string;
  fromName: string;
  side: "self" | "opponent";
  styleToken: string;
}

/** 服务端只下发受控 token；客户端静态映射视觉，绝不执行服务端提供的 CSS。 */
function chatDecorationBubbleClass(styleToken: string): string {
  switch (styleToken) {
    case "sunset":
      return "border-orange-300/70 bg-gradient-to-br from-orange-950/95 via-rose-950/95 to-slate-950/95 text-orange-50 ring-1 ring-orange-300/25";
    case "tide":
      return "border-cyan-300/70 bg-gradient-to-br from-cyan-950/95 via-blue-950/95 to-slate-950/95 text-cyan-50 ring-1 ring-cyan-300/25";
    case "gold":
      return "border-yellow-300/75 bg-gradient-to-br from-yellow-950/95 via-amber-950/95 to-slate-950/95 text-yellow-50 ring-1 ring-yellow-300/25";
    case "haki":
      return "border-fuchsia-300/75 bg-gradient-to-br from-fuchsia-950/95 via-violet-950/95 to-slate-950/95 text-fuchsia-50 ring-1 ring-fuchsia-300/30";
    case "leaf":
      return "border-emerald-300/70 bg-gradient-to-br from-emerald-950/95 via-teal-950/95 to-slate-950/95 text-emerald-50 ring-1 ring-emerald-300/25";
    case "feast":
      return "border-lime-300/70 bg-gradient-to-br from-lime-950/95 via-orange-950/95 to-slate-950/95 text-lime-50 ring-1 ring-lime-300/25";
    case "shock":
      return "border-sky-200/75 bg-gradient-to-br from-sky-950/95 via-indigo-950/95 to-slate-950/95 text-sky-50 ring-1 ring-sky-200/30";
    case "wanted":
      return "border-amber-200/80 bg-gradient-to-br from-stone-800/95 via-amber-950/95 to-red-950/95 text-amber-50 ring-1 ring-amber-200/35";
    case "mist":
      return "border-slate-300/65 bg-gradient-to-br from-slate-700/95 via-slate-900/95 to-gray-950/95 text-slate-50 ring-1 ring-slate-200/20";
    case "navy":
      return "border-blue-300/70 bg-gradient-to-br from-blue-950/95 via-slate-950/95 to-gray-950/95 text-blue-50 ring-1 ring-blue-300/25";
    case "ember":
      return "border-red-300/75 bg-gradient-to-br from-red-950/95 via-orange-950/95 to-slate-950/95 text-red-50 ring-1 ring-red-300/30";
    case "emperor":
      return "border-purple-200/80 bg-gradient-to-br from-purple-950/95 via-red-950/95 to-black/95 text-purple-50 ring-2 ring-purple-300/35";
    default:
      return "border-slate-300/60 bg-slate-950/95 text-white ring-1 ring-white/15";
  }
}

type ActiveControl = "chat" | "friends" | "spectators" | "more" | null;

export default function GameChatPanel({
  isPlayback,
  isObserver,
  onOpenFeedback,
}: {
  isPlayback: boolean;
  isObserver: boolean;
  onOpenFeedback?: () => void;
}) {
  const rotateQuarterTurn = useLayoutQuarterTurn();
  const { t } = useLanguage();
  const myAccount = useNetStore((s) => s.account);
  const friendChatUnreadByAccount = useNetStore(
    (s) => s.friendChatUnreadByAccount,
  );
  const incomingFriendCount = useNetStore(
    (s) => s.incomingFriendRequests.length,
  );
  const chatDecorationSnapshot = useNetStore(
    (s) => s.chatDecorationExchange.snapshot,
  );
  const opponentName = useGameStore((s) => s.opponentName);
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
  const [activeControl, setActiveControl] = useState<ActiveControl>(null);
  const [muted, setMuted] = useState(false);
  const [gameInput, setGameInput] = useState("");
  const [gameMessages, setGameMessages] = useState<GameChatItem[]>([]);
  const [coolingDown, setCoolingDown] = useState(false);
  const [gameUnread, setGameUnread] = useState(0);
  const [spectatorHovered, setSpectatorHovered] = useState(false);
  const [toast, setToast] = useState<ChatToast | null>(null);
  const [cooldownSeconds, setCooldownSeconds] = useState(0);
  const [kickConfirm, setKickConfirm] = useState("");
  const [decorationBubbles, setDecorationBubbles] = useState<Partial<
    Record<"self" | "opponent", DecorationBubble>
  >>({});

  const open = activeControl === "chat";
  const friendsOpen = activeControl === "friends";
  const spectatorPinned = activeControl === "spectators";

  const idRef = useRef(0);
  const mutedRef = useRef(muted);
  const accountRef = useRef(myAccount);
  const openRef = useRef(open);
  const listRef = useRef<HTMLDivElement>(null);
  const toastTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const cooldownTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const decorationBubbleTimers = useRef<Partial<
    Record<"self" | "opponent", ReturnType<typeof setTimeout>>
  >>({});

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
    if (spectatorNames.length === 0 && activeControl === "spectators") {
      setActiveControl(null);
    }
  }, [activeControl, spectatorNames.length]);

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
      displaySide?: "self" | "opponent" | null;
      decoration?: {
        id: string;
        styleToken: string;
      } | null;
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
        decoration: message.decoration,
      };
      setGameMessages((previous) => [...previous.slice(-49), item]);
      if (message.decoration) {
        const side = message.displaySide === "self" || message.displaySide === "opponent"
          ? message.displaySide
          : isSelf ? "self" : "opponent";
        const bubble: DecorationBubble = {
          id: item.id,
          text: item.text,
          fromName: item.fromName,
          side,
          styleToken: message.decoration.styleToken,
        };
        setDecorationBubbles((previous) => ({ ...previous, [side]: bubble }));
        const previousTimer = decorationBubbleTimers.current[side];
        if (previousTimer) clearTimeout(previousTimer);
        decorationBubbleTimers.current[side] = setTimeout(() => {
          setDecorationBubbles((previous) => {
            if (previous[side]?.id !== bubble.id) return previous;
            const next = { ...previous };
            delete next[side];
            return next;
          });
          delete decorationBubbleTimers.current[side];
        }, DECORATION_BUBBLE_MS);
      } else if (!isSelf && !openRef.current) {
        setGameUnread((count) => count + 1);
        showToast({
          text: item.text,
          fromName: item.fromName,
          fromRole: item.fromRole,
        });
      }
    };
    eventBus.on("gameChat", handler);
    return () => {
      eventBus.off("gameChat", handler);
      if (toastTimer.current) clearTimeout(toastTimer.current);
      if (cooldownTimer.current) clearTimeout(cooldownTimer.current);
      Object.values(decorationBubbleTimers.current).forEach((timer) => {
        if (timer) clearTimeout(timer);
      });
      decorationBubbleTimers.current = {};
    };
  }, []);

  useEffect(() => {
    if (!open || !listRef.current) return;
    listRef.current.scrollTop = listRef.current.scrollHeight;
  }, [gameMessages, open]);

  if (isPlayback) return null;

  const spectatorDetailsForViewer = isObserver ? [] : spectatorDetails;
  const showSpectatorIndicator = spectatorNames.length > 0;
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

  const sendDecoration = (slot: ChatDecorationSlot) => {
    if (coolingDown || isObserver) return;
    if (GameRequest.sendChatDecoration(slot)) fireCooldown();
  };

  const equippedDecorations = new Map<ChatDecorationSlot, ChatDecorationItem>(
    (chatDecorationSnapshot?.items ?? [])
      .filter((item) => item.owned && item.equipped)
      .map((item) => [item.slot, item]),
  );

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

  const handleMoreOpenChange = useCallback((nextOpen: boolean) => {
    setActiveControl((current) => nextOpen ? "more" : current === "more" ? null : current);
  }, []);

  return (
    <>
      <div
        data-chat-decoration-bubble-layer
        aria-live="polite"
        className="pointer-events-none fixed inset-0 z-[45] overflow-hidden"
      >
        {(["opponent", "self"] as const).map((side) => {
          const bubble = decorationBubbles[side];
          if (!bubble) return null;
          return (
            <div
              key={`${side}:${bubble.id}`}
              data-chat-decoration-bubble
              data-display-side={side}
              data-style-token={bubble.styleToken}
              role="status"
              className={`absolute right-[calc(clamp(8rem,19cqw,14rem)+var(--layout-safe-right,0px))] w-[min(17rem,38cqw)] rounded-2xl border px-4 py-3 text-sm font-bold leading-relaxed shadow-2xl backdrop-blur ${
                side === "self"
                  ? "bottom-[calc(18%+var(--layout-safe-bottom,0px))]"
                  : "top-[calc(18%+var(--layout-safe-top,0px))]"
              } ${chatDecorationBubbleClass(bubble.styleToken)}`}
            >
              <span className="mb-1 block text-[10px] font-black uppercase tracking-[0.18em] opacity-75">
                {side === "self" ? "你的装饰" : bubble.fromName}
              </span>
              <span>{t(bubble.text)}</span>
              <span
                aria-hidden="true"
                className="absolute -right-2 top-1/2 h-4 w-4 -translate-y-1/2 rotate-45 border-r border-t border-current bg-inherit opacity-80"
              />
            </div>
          );
        })}
      </div>
      {showSpectatorIndicator && (
        <SpectatorArena
          spectatorNames={spectatorNames}
          spectatorDetails={spectatorDetailsForViewer}
          muted={muted}
          onKick={kickSpectator}
          kickConfirm={kickConfirm}
        />
      )}
      <div
        data-game-chat-root
        data-layout-rotated={rotateQuarterTurn ? "true" : "false"}
        data-visual-anchor="left-bottom"
        className="pointer-events-none fixed z-50"
        style={{
          ...(rotateQuarterTurn
            ? {
                right:
                  "max(0.75rem, var(--layout-safe-right, env(safe-area-inset-right)))",
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
          <div
            data-game-chat-toast
            className={`pointer-events-none max-w-[240px] rounded-lg bg-black/80 px-3 py-1.5 text-xs text-white shadow-lg ring-1 ring-white/15 ${toast.fromRole === "spectator" && !isObserver ? "md:hidden" : ""}`}
            style={
              rotateQuarterTurn
                ? {
                    width:
                      "min(15rem, calc(100cqw - 5.25rem - var(--layout-safe-left, 0px) - var(--layout-safe-right, 0px)))",
                  }
                : undefined
            }
          >
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
                  onClick={() => setActiveControl(null)}
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
                <div
                  key={message.id}
                  className={`mb-1 leading-snug ${message.decoration ? "rounded-md border border-amber-400/20 bg-amber-400/5 px-2 py-1" : ""}`}
                >
                  <span
                    className={`font-bold ${message.isSelf ? "text-sky-300" : message.fromRole === "spectator" ? "text-slate-400" : "text-amber-300"}`}
                  >
                    {message.isSelf ? "你" : message.fromName}
                    {message.fromRole === "spectator" ? "(观战)" : ""}：
                  </span>
                  <span className="text-slate-100">{t(message.text)}</span>
                </div>
              ))}
            </div>
            {!isObserver && (
              <div
                data-chat-decoration-quickbar
                className="shrink-0 border-t border-amber-300/15 bg-amber-950/15 px-2 py-2"
              >
                <div className="mb-1 flex items-center justify-between gap-2 px-1">
                  <span className="text-[10px] font-black uppercase tracking-wider text-amber-200">
                    特殊聊天装饰
                  </span>
                  <span className="text-[10px] text-slate-500">文案与样式由服务器确认</span>
                </div>
                <div className="flex touch-pan-x gap-1.5 overflow-x-auto overscroll-x-contain pb-1">
                  {DECORATION_SLOTS.map(({ slot, label, icon }) => {
                    const decoration = equippedDecorations.get(slot);
                    return (
                      <button
                        key={slot}
                        type="button"
                        onClick={() => sendDecoration(slot)}
                        disabled={coolingDown || !decoration}
                        aria-label={decoration ? `${label}：${t(decoration.name)}` : `${label}：未装配`}
                        title={decoration ? t(decoration.text) : "请先在交易所购买并装配"}
                        className="flex min-h-12 min-w-[5.25rem] shrink-0 flex-col items-center justify-center rounded-xl border border-amber-300/20 bg-slate-800/90 px-2 text-[10px] font-bold text-amber-50 transition-colors hover:border-amber-300/45 hover:bg-slate-700 disabled:cursor-not-allowed disabled:border-white/5 disabled:text-slate-600 disabled:opacity-70"
                      >
                        <span aria-hidden="true" className="text-sm">{icon}</span>
                        <span>{label}</span>
                        <span className="max-w-16 truncate text-[9px] font-normal opacity-70">
                          {decoration ? t(decoration.name) : "未装配"}
                        </span>
                      </button>
                    );
                  })}
                </div>
              </div>
            )}
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
          data-game-control-dock
          className={`pointer-events-auto flex gap-1 ${rotateQuarterTurn ? "flex-col items-end" : "items-center"}`}
        >
          <button
            type="button"
            onClick={() => {
              setActiveControl((current) => current === "chat" ? null : "chat");
              if (!open) setGameUnread(0);
            }}
            className="relative flex h-12 w-12 items-center justify-center rounded-full text-base focus-visible:outline-2 focus-visible:outline-sky-300"
            title="局内聊天"
            aria-label="打开局内聊天"
            aria-expanded={open}
          >
            <span className="flex h-9 w-9 items-center justify-center rounded-full bg-slate-800/90 shadow-lg ring-1 ring-white/15 transition-colors hover:bg-slate-700">
              💬
            </span>
            {!open && gameUnread > 0 && (
              <span className="absolute -right-1 -top-1 flex h-5 min-w-5 items-center justify-center rounded-full bg-rose-500 px-1 text-[10px] font-bold text-white ring-2 ring-slate-900">
                {gameUnread > 9 ? "9+" : gameUnread}
              </span>
            )}
          </button>

          <button
            type="button"
            onClick={() => {
              setActiveControl("friends");
            }}
            className="relative flex h-12 w-12 items-center justify-center rounded-full text-emerald-50 focus-visible:outline-2 focus-visible:outline-emerald-300"
            title="好友中心"
            aria-label={`打开好友中心${totalFriendUnread ? `，${totalFriendUnread} 条好友消息` : ""}${incomingFriendCount ? `，${incomingFriendCount} 条新申请` : ""}`}
            aria-expanded={friendsOpen}
          >
            <span className="flex h-9 w-9 items-center justify-center rounded-full bg-emerald-800/90 shadow-lg ring-1 ring-emerald-300/25 transition-colors hover:bg-emerald-700">
              <svg
                viewBox="0 0 24 24"
                className="h-4 w-4"
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
            </span>
            {friendAlertCount > 0 && (
              <span className="absolute -right-1 -top-1 flex h-5 min-w-5 items-center justify-center rounded-full bg-rose-500 px-1 text-[10px] font-bold text-white ring-2 ring-slate-900">
                {friendAlertCount > 9 ? "9+" : friendAlertCount}
              </span>
            )}
          </button>

          {showSpectatorIndicator && (
            <div
              className="relative md:hidden"
              onMouseEnter={() => setSpectatorHovered(true)}
              onMouseLeave={() => setSpectatorHovered(false)}
            >
              {showSpectatorList && (
                <div className="absolute bottom-full left-0 mb-2 w-52 rounded-xl bg-slate-900/95 p-3 text-xs text-white shadow-2xl ring-1 ring-purple-300/25">
                  <p className="mb-2 font-bold text-purple-200">
                    {spectatorNames.length} 人正在观战
                  </p>
                  <div className="max-h-40 space-y-1 overflow-y-auto">
                    {(spectatorDetailsForViewer.length > 0
                      ? spectatorDetailsForViewer
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
                        {spectatorDetailsForViewer.length > 0 && (
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
                data-mobile-spectator-trigger
                onClick={() => {
                  setActiveControl((current) => current === "spectators" ? null : "spectators");
                }}
                className="relative flex h-12 w-12 items-center justify-center rounded-full text-purple-100 focus-visible:outline-2 focus-visible:outline-purple-300"
                title={`${spectatorNames.length} 人正在观战`}
                aria-label={`查看正在观战的 ${spectatorNames.length} 人`}
                aria-expanded={showSpectatorList}
              >
                <span className="flex h-9 w-9 items-center justify-center rounded-full bg-purple-900/90 shadow-lg ring-1 ring-purple-300/30 transition-colors hover:bg-purple-800">
                  <svg
                    viewBox="0 0 24 24"
                    aria-hidden="true"
                    className="h-4 w-4 fill-none stroke-current stroke-2"
                  >
                    <path d="M2.5 12s3.5-6 9.5-6 9.5 6 9.5 6-3.5 6-9.5 6-9.5-6-9.5-6Z" />
                    <circle cx="12" cy="12" r="2.75" />
                  </svg>
                </span>
                <span className="absolute -right-1 -top-1 flex h-5 min-w-5 items-center justify-center rounded-full bg-purple-500 px-1 text-[10px] font-bold text-white ring-2 ring-slate-900">
                  {spectatorNames.length > 99 ? "99+" : spectatorNames.length}
                </span>
              </button>
            </div>
          )}
          {onOpenFeedback && (
            <GameMenu
              open={activeControl === "more"}
              onOpenChange={handleMoreOpenChange}
              onOpenFeedback={onOpenFeedback}
              targetName={opponentName || "对手"}
              playerToolsEnabled={!isObserver}
            />
          )}
          {!isObserver && <MobileTurnExtensionButton />}
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
      <FriendsPanel
        open={friendsOpen}
        onClose={() => setActiveControl((current) => current === "friends" ? null : current)}
      />
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
