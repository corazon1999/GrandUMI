"use client";

import { useEffect, useRef, useState } from "react";
import { eventBus } from "@/net/eventBus";
import { GameRequest } from "@/net/GameRequest";
import { useNetStore } from "@/store/netStore";
import { useGameStore } from "@/store/gameStore";

/**
 * 局内聊天（房间内：对战双方 + 观战者）。
 * 预设短语 + 自由文字；静音开关；可展开历史栏；新消息冒泡 ~4s 自动淡出。
 * 走 MsgGameChat（房间内中继），区别于大厅全局聊天 MsgChatMsg。回放模式不渲染。
 */

const PRESETS = ["你好", "好牌！", "谢谢", "手下留情", "该你了", "认输吧", "GG", "网络卡了，稍等"];
const COOLDOWN_MS = 1300;

interface ChatItem {
  id: number;
  text: string;
  fromName: string;
  isSelf: boolean;
  fromRole: "player" | "spectator";
}

export default function GameChatPanel({ isPlayback, isObserver }: { isPlayback: boolean; isObserver: boolean }) {
  const myAccount = useNetStore((s) => s.account);
  const spectatorNames = useGameStore((s) => s.spectatorNames);
  const [open, setOpen] = useState(false);
  const [muted, setMuted] = useState(false);
  const [input, setInput] = useState("");
  const [messages, setMessages] = useState<ChatItem[]>([]);
  const [coolingDown, setCoolingDown] = useState(false);
  const [unread, setUnread] = useState(0);
  const [spectatorHovered, setSpectatorHovered] = useState(false);
  const [spectatorPinned, setSpectatorPinned] = useState(false);
  // 最近一条用于"冒泡"展示（关闭面板时也能看到对方说话）
  const [toast, setToast] = useState<ChatItem | null>(null);

  const idRef = useRef(0);
  const mutedRef = useRef(muted);
  const accountRef = useRef(myAccount);
  const openRef = useRef(open);
  const listRef = useRef<HTMLDivElement>(null);
  const toastTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const cooldownTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => { mutedRef.current = muted; }, [muted]);
  useEffect(() => { accountRef.current = myAccount; }, [myAccount]);
  useEffect(() => { openRef.current = open; if (open) setUnread(0); }, [open]);
  useEffect(() => {
    if (spectatorNames.length === 0) setSpectatorPinned(false);
  }, [spectatorNames.length]);

  // 订阅服务器推送的局内聊天
  useEffect(() => {
    const handler = (m: {
      text: string; fromAccount?: string; fromName: string; fromRole: "player" | "spectator";
    }) => {
      const isSelf = !!m.fromAccount && m.fromAccount === accountRef.current;
      if (mutedRef.current && !isSelf) return; // 静音：忽略他人消息（自己的仍显示）
      const item: ChatItem = {
        id: ++idRef.current,
        text: m.text,
        fromName: m.fromName,
        isSelf,
        fromRole: m.fromRole,
      };
      setMessages((prev) => [...prev.slice(-49), item]);
      if (!openRef.current) {
        if (!isSelf) setUnread((u) => u + 1);
        // 冒泡提示（自己发的不冒泡）
        if (!isSelf) {
          setToast(item);
          if (toastTimer.current) clearTimeout(toastTimer.current);
          toastTimer.current = setTimeout(() => setToast(null), 4000);
        }
      }
    };
    eventBus.on("gameChat", handler);
    return () => {
      eventBus.off("gameChat", handler);
      if (toastTimer.current) clearTimeout(toastTimer.current);
      if (cooldownTimer.current) clearTimeout(cooldownTimer.current);
    };
  }, []);

  // 新消息时滚到底
  useEffect(() => {
    if (open && listRef.current) listRef.current.scrollTop = listRef.current.scrollHeight;
  }, [messages, open]);

  if (isPlayback) return null;

  const showSpectatorIndicator = !isObserver && spectatorNames.length > 0;
  const showSpectatorList = showSpectatorIndicator && (spectatorHovered || spectatorPinned);

  const fireCooldown = () => {
    setCoolingDown(true);
    if (cooldownTimer.current) clearTimeout(cooldownTimer.current);
    cooldownTimer.current = setTimeout(() => setCoolingDown(false), COOLDOWN_MS);
  };

  const sendPreset = (text: string) => {
    if (coolingDown) return;
    GameRequest.sendGameChat(text, "preset");
    fireCooldown();
  };

  const sendFree = () => {
    const t = input.trim();
    if (!t || coolingDown) return;
    GameRequest.sendGameChat(t);
    setInput("");
    fireCooldown();
  };

  return (
    <div className="pointer-events-none fixed bottom-3 left-3 z-50 flex flex-col items-start gap-2 max-md:bottom-2 max-md:left-2">
      {/* 关闭时的新消息冒泡 */}
      {!open && toast && (
        <div className="pointer-events-none max-w-[220px] rounded-lg bg-black/80 px-3 py-1.5 text-xs text-white shadow-lg ring-1 ring-white/15">
          <span className="font-bold text-amber-300">{toast.fromName}：</span>
          {toast.text}
        </div>
      )}

      {/* 聊天面板 */}
      {open && (
        <div className="pointer-events-auto flex w-72 flex-col overflow-hidden rounded-xl bg-slate-900/95 shadow-2xl ring-1 ring-white/15 max-md:w-60">
          <div className="flex items-center justify-between border-b border-white/10 px-3 py-1.5">
            <span className="text-xs font-bold text-slate-200">局内聊天</span>
            <div className="flex items-center gap-2">
              <button
                onClick={() => setMuted((v) => !v)}
                className={`text-xs ${muted ? "text-rose-400" : "text-slate-400 hover:text-slate-200"}`}
                title={muted ? "已静音对手（点击取消）" : "静音对手"}
              >
                {muted ? "🔇 已静音" : "🔊"}
              </button>
              <button onClick={() => setOpen(false)} className="text-slate-400 hover:text-white" title="收起">✕</button>
            </div>
          </div>

          {/* 历史栏 */}
          <div ref={listRef} className="h-40 overflow-y-auto px-3 py-2 text-xs">
            {messages.length === 0 && <div className="text-slate-500">还没有消息。发个招呼吧～</div>}
            {messages.map((m) => (
              <div key={m.id} className="mb-1 leading-snug">
                <span className={`font-bold ${m.isSelf ? "text-sky-300" : m.fromRole === "spectator" ? "text-slate-400" : "text-amber-300"}`}>
                  {m.isSelf ? "你" : m.fromName}{m.fromRole === "spectator" ? "(观战)" : ""}：
                </span>
                <span className="text-slate-100">{m.text}</span>
              </div>
            ))}
          </div>

          {/* 预设短语 */}
          <div className="flex flex-wrap gap-1 border-t border-white/10 px-2 py-1.5">
            {PRESETS.map((p) => (
              <button
                key={p}
                onClick={() => sendPreset(p)}
                disabled={coolingDown}
                className="rounded-full bg-slate-700/80 px-2 py-0.5 text-[11px] text-slate-100 hover:bg-slate-600 disabled:opacity-40"
              >
                {p}
              </button>
            ))}
          </div>

          {/* 自由文字输入 */}
          <div className="flex items-center gap-1 border-t border-white/10 p-2">
            <input
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter") sendFree(); }}
              maxLength={100}
              placeholder="输入消息…"
              className="min-w-0 flex-1 rounded-md bg-slate-800 px-2 py-1 text-xs text-white outline-none ring-1 ring-white/10 focus:ring-sky-400"
            />
            <button
              onClick={sendFree}
              disabled={coolingDown || !input.trim()}
              className="rounded-md bg-sky-600 px-3 py-1 text-xs font-bold text-white hover:bg-sky-500 disabled:bg-slate-700 disabled:opacity-50"
            >
              发送
            </button>
          </div>
        </div>
      )}

      {/* 左下角控制按钮 */}
      <div className="pointer-events-auto flex items-center gap-2">
        <button
          onClick={() => {
            setSpectatorPinned(false);
            setOpen((v) => !v);
          }}
          className="relative flex h-10 w-10 items-center justify-center rounded-full bg-slate-800/90 text-lg shadow-lg ring-1 ring-white/15 hover:bg-slate-700"
          title="局内聊天"
        >
          💬
          {!open && unread > 0 && (
            <span className="absolute -right-1 -top-1 flex h-5 min-w-5 items-center justify-center rounded-full bg-rose-500 px-1 text-[10px] font-bold text-white ring-2 ring-slate-900">
              {unread > 9 ? "9+" : unread}
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
                  {spectatorNames.map((name, index) => (
                    <div key={`${name}:${index}`} className="truncate rounded-md bg-white/5 px-2 py-1 text-slate-200">
                      {name}
                    </div>
                  ))}
                </div>
              </div>
            )}
            <button
              onClick={() => {
                setOpen(false);
                setSpectatorPinned((v) => !v);
              }}
              className="relative flex h-10 w-10 items-center justify-center rounded-full bg-purple-900/90 text-purple-100 shadow-lg ring-1 ring-purple-300/30 transition-colors hover:bg-purple-800"
              title={`${spectatorNames.length} 人正在观战`}
              aria-label={`查看正在观战的 ${spectatorNames.length} 人`}
              aria-expanded={showSpectatorList}
            >
              <svg viewBox="0 0 24 24" aria-hidden="true" className="h-5 w-5 fill-none stroke-current stroke-2">
                <path d="M2.5 12s3.5-6 9.5-6 9.5 6 9.5 6-3.5 6-9.5 6-9.5-6-9.5-6Z" />
                <circle cx="12" cy="12" r="2.75" />
              </svg>
              <span className="absolute -right-1 -top-1 flex h-5 min-w-5 items-center justify-center rounded-full bg-purple-500 px-1 text-[10px] font-bold text-white ring-2 ring-slate-900">
                {spectatorNames.length > 99 ? "99+" : spectatorNames.length}
              </span>
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
