"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { eventBus } from "@/net/eventBus";
import { GameRequest } from "@/net/GameRequest";
import { HomeRequest } from "@/net/HomeProtocol";
import { useNetStore } from "@/store/netStore";
import { useGameStore } from "@/store/gameStore";

/** 对局聊天与好友实时私聊共用的左下角分页面板。 */

const PRESETS = ["你好", "好牌！", "谢谢", "手下留情", "该你了", "认输吧", "GG", "网络卡了，稍等"];
const COOLDOWN_MS = 1300;

type ChatTab = "game" | "friends";

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
  kind: ChatTab;
}

export default function GameChatPanel({ isPlayback, isObserver }: { isPlayback: boolean; isObserver: boolean }) {
  const myAccount = useNetStore((s) => s.account);
  const friends = useNetStore((s) => s.friends);
  const friendChatMessages = useNetStore((s) => s.friendChatMessages);
  const spectatorNames = useGameStore((s) => s.spectatorNames);
  const [open, setOpen] = useState(false);
  const [activeTab, setActiveTab] = useState<ChatTab>("game");
  const [muted, setMuted] = useState(false);
  const [gameInput, setGameInput] = useState("");
  const [friendInput, setFriendInput] = useState("");
  const [gameMessages, setGameMessages] = useState<GameChatItem[]>([]);
  const [coolingDown, setCoolingDown] = useState(false);
  const [gameUnread, setGameUnread] = useState(0);
  const [friendUnread, setFriendUnread] = useState<Record<string, number>>({});
  const [selectedFriendAccount, setSelectedFriendAccount] = useState("");
  const [spectatorHovered, setSpectatorHovered] = useState(false);
  const [spectatorPinned, setSpectatorPinned] = useState(false);
  const [toast, setToast] = useState<ChatToast | null>(null);

  const idRef = useRef(0);
  const mutedRef = useRef(muted);
  const accountRef = useRef(myAccount);
  const openRef = useRef(open);
  const activeTabRef = useRef<ChatTab>(activeTab);
  const selectedFriendRef = useRef(selectedFriendAccount);
  const processedFriendMessages = useRef(new Set(friendChatMessages.map((message) => message.id)));
  const listRef = useRef<HTMLDivElement>(null);
  const toastTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const cooldownTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const selectedFriend = useMemo(() => friends.find(
    (friend) => friend.account.toLocaleLowerCase("zh-CN") === selectedFriendAccount.toLocaleLowerCase("zh-CN"),
  ), [friends, selectedFriendAccount]);

  const selectedFriendMessages = useMemo(() => {
    if (!selectedFriendAccount) return [];
    const accountKey = selectedFriendAccount.toLocaleLowerCase("zh-CN");
    return friendChatMessages.filter((message) => {
      const fromKey = message.fromAccount.toLocaleLowerCase("zh-CN");
      const toKey = message.toAccount.toLocaleLowerCase("zh-CN");
      return fromKey === accountKey || toKey === accountKey;
    });
  }, [friendChatMessages, selectedFriendAccount]);

  const totalFriendUnread = Object.values(friendUnread).reduce((total, count) => total + count, 0);
  const totalUnread = gameUnread + totalFriendUnread;

  useEffect(() => {
    HomeRequest.requestFriendList();
  }, []);

  useEffect(() => {
    if (friends.length === 0) {
      setSelectedFriendAccount("");
      return;
    }
    if (selectedFriend) return;
    const firstOnline = friends.find((friend) => friend.online) ?? friends[0];
    setSelectedFriendAccount(firstOnline.account);
  }, [friends, selectedFriend]);

  useEffect(() => { mutedRef.current = muted; }, [muted]);
  useEffect(() => { accountRef.current = myAccount; }, [myAccount]);
  useEffect(() => { openRef.current = open; }, [open]);
  useEffect(() => { activeTabRef.current = activeTab; }, [activeTab]);
  useEffect(() => { selectedFriendRef.current = selectedFriendAccount; }, [selectedFriendAccount]);
  useEffect(() => {
    if (spectatorNames.length === 0) setSpectatorPinned(false);
  }, [spectatorNames.length]);

  const showToast = (nextToast: ChatToast) => {
    setToast(nextToast);
    if (toastTimer.current) clearTimeout(toastTimer.current);
    toastTimer.current = setTimeout(() => setToast(null), 4000);
  };

  useEffect(() => {
    const handler = (message: {
      text: string; fromAccount?: string; fromName: string; fromRole: "player" | "spectator";
    }) => {
      const isSelf = !!message.fromAccount && message.fromAccount === accountRef.current;
      if (mutedRef.current && !isSelf) return;
      const item: GameChatItem = {
        id: ++idRef.current,
        text: message.text,
        fromName: message.fromName,
        isSelf,
        fromRole: message.fromRole,
      };
      setGameMessages((previous) => [...previous.slice(-49), item]);
      if (!isSelf && (!openRef.current || activeTabRef.current !== "game")) {
        setGameUnread((count) => count + 1);
        showToast({ text: item.text, fromName: item.fromName, kind: "game" });
      }
    };
    eventBus.on("gameChat", handler);
    return () => {
      eventBus.off("gameChat", handler);
      if (toastTimer.current) clearTimeout(toastTimer.current);
      if (cooldownTimer.current) clearTimeout(cooldownTimer.current);
    };
  }, []);

  useEffect(() => {
    for (const message of friendChatMessages) {
      if (processedFriendMessages.current.has(message.id)) continue;
      processedFriendMessages.current.add(message.id);
      const isSelf = message.fromAccount.toLocaleLowerCase("zh-CN") === accountRef.current.toLocaleLowerCase("zh-CN");
      if (isSelf) continue;
      const conversationAccount = message.fromAccount;
      const isViewingConversation = openRef.current
        && activeTabRef.current === "friends"
        && selectedFriendRef.current.toLocaleLowerCase("zh-CN") === conversationAccount.toLocaleLowerCase("zh-CN");
      if (!isViewingConversation) {
        setFriendUnread((previous) => ({
          ...previous,
          [conversationAccount]: (previous[conversationAccount] ?? 0) + 1,
        }));
        showToast({ text: message.text, fromName: message.fromName, kind: "friends" });
      }
    }
  }, [friendChatMessages]);

  useEffect(() => {
    if (!open || !listRef.current) return;
    listRef.current.scrollTop = listRef.current.scrollHeight;
  }, [activeTab, friendChatMessages, gameMessages, open, selectedFriendAccount]);

  if (isPlayback) return null;

  const showSpectatorIndicator = !isObserver && spectatorNames.length > 0;
  const showSpectatorList = showSpectatorIndicator && (spectatorHovered || spectatorPinned);

  const fireCooldown = () => {
    setCoolingDown(true);
    if (cooldownTimer.current) clearTimeout(cooldownTimer.current);
    cooldownTimer.current = setTimeout(() => setCoolingDown(false), COOLDOWN_MS);
  };

  const selectTab = (tab: ChatTab) => {
    setActiveTab(tab);
    if (tab === "game") setGameUnread(0);
    if (tab === "friends" && selectedFriendAccount) {
      setFriendUnread((previous) => ({ ...previous, [selectedFriendAccount]: 0 }));
    }
  };

  const selectFriend = (account: string) => {
    setSelectedFriendAccount(account);
    setFriendUnread((previous) => ({ ...previous, [account]: 0 }));
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

  const sendFriendMessage = () => {
    const text = friendInput.trim();
    if (!text || !selectedFriend?.online || coolingDown) return;
    GameRequest.sendFriendChat(selectedFriend.account, text);
    setFriendInput("");
    fireCooldown();
  };

  return (
    <div className="pointer-events-none fixed bottom-3 left-3 z-50 flex flex-col items-start gap-2 max-md:bottom-2 max-md:left-2">
      {!open && toast && (
        <div className="pointer-events-none max-w-[240px] rounded-lg bg-black/80 px-3 py-1.5 text-xs text-white shadow-lg ring-1 ring-white/15">
          <span className={`font-bold ${toast.kind === "friends" ? "text-sky-300" : "text-amber-300"}`}>
            {toast.kind === "friends" ? "好友 · " : ""}{toast.fromName}：
          </span>
          {toast.text}
        </div>
      )}

      {open && (
        <div className="pointer-events-auto flex w-80 flex-col overflow-hidden rounded-xl bg-slate-900/95 shadow-2xl ring-1 ring-white/15 max-md:w-64">
          <div className="flex items-center justify-between border-b border-white/10 px-3 py-1.5">
            <span className="text-xs font-bold text-slate-200">聊天</span>
            <div className="flex items-center gap-2">
              {activeTab === "game" && (
                <button
                  type="button"
                  onClick={() => setMuted((value) => !value)}
                  className={`text-xs ${muted ? "text-rose-400" : "text-slate-400 hover:text-slate-200"}`}
                  title={muted ? "已静音局内其他玩家（点击取消）" : "静音局内其他玩家"}
                >
                  {muted ? "🔇 已静音" : "🔊"}
                </button>
              )}
              <button type="button" onClick={() => setOpen(false)} className="text-slate-400 hover:text-white" title="收起">✕</button>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-1 border-b border-white/10 bg-slate-950/70 p-1">
            <button type="button" onClick={() => selectTab("game")} className={`relative rounded-md py-1.5 text-xs font-bold ${activeTab === "game" ? "bg-amber-600 text-white" : "text-slate-400 hover:bg-white/5"}`}>
              局内
              {gameUnread > 0 && <span className="ml-1 rounded-full bg-rose-500 px-1.5 py-0.5 text-[9px] text-white">{gameUnread > 9 ? "9+" : gameUnread}</span>}
            </button>
            <button type="button" onClick={() => selectTab("friends")} className={`relative rounded-md py-1.5 text-xs font-bold ${activeTab === "friends" ? "bg-sky-700 text-white" : "text-slate-400 hover:bg-white/5"}`}>
              好友
              {totalFriendUnread > 0 && <span className="ml-1 rounded-full bg-rose-500 px-1.5 py-0.5 text-[9px] text-white">{totalFriendUnread > 9 ? "9+" : totalFriendUnread}</span>}
            </button>
          </div>

          {activeTab === "game" ? (
            <>
              <div ref={listRef} className="h-40 overflow-y-auto px-3 py-2 text-xs">
                {gameMessages.length === 0 && <div className="text-slate-500">还没有消息。发个招呼吧～</div>}
                {gameMessages.map((message) => (
                  <div key={message.id} className="mb-1 leading-snug">
                    <span className={`font-bold ${message.isSelf ? "text-sky-300" : message.fromRole === "spectator" ? "text-slate-400" : "text-amber-300"}`}>
                      {message.isSelf ? "你" : message.fromName}{message.fromRole === "spectator" ? "(观战)" : ""}：
                    </span>
                    <span className="text-slate-100">{message.text}</span>
                  </div>
                ))}
              </div>
              <div className="flex flex-wrap gap-1 border-t border-white/10 px-2 py-1.5">
                {PRESETS.map((preset) => (
                  <button key={preset} type="button" onClick={() => sendPreset(preset)} disabled={coolingDown} className="rounded-full bg-slate-700/80 px-2 py-0.5 text-[11px] text-slate-100 hover:bg-slate-600 disabled:opacity-40">
                    {preset}
                  </button>
                ))}
              </div>
              <ChatInput value={gameInput} onChange={setGameInput} onSend={sendGameMessage} disabled={coolingDown} placeholder="输入局内消息…" />
            </>
          ) : (
            <>
              {friends.length > 0 ? (
                <>
                  <div className="border-b border-white/10 p-2">
                    <select
                      value={selectedFriendAccount}
                      onChange={(event) => selectFriend(event.target.value)}
                      aria-label="选择聊天好友"
                      className="h-9 w-full rounded-md bg-slate-800 px-2 text-xs text-white outline-none ring-1 ring-white/10 focus:ring-sky-400"
                    >
                      {friends.map((friend) => (
                        <option key={friend.account} value={friend.account}>
                          {friend.online ? "●" : "○"} {friend.name} (@{friend.account}){friendUnread[friend.account] ? ` · ${friendUnread[friend.account]} 条未读` : ""}
                        </option>
                      ))}
                    </select>
                  </div>
                  <div ref={listRef} className="h-48 overflow-y-auto px-3 py-2 text-xs">
                    {selectedFriendMessages.length === 0 && (
                      <div className="text-slate-500">还没有消息。{selectedFriend?.online ? "打个招呼吧～" : "好友上线后就可以聊天。"}</div>
                    )}
                    {selectedFriendMessages.map((message) => {
                      const isSelf = message.fromAccount.toLocaleLowerCase("zh-CN") === myAccount.toLocaleLowerCase("zh-CN");
                      return (
                        <div key={message.id} className="mb-1 leading-snug">
                          <span className={`font-bold ${isSelf ? "text-sky-300" : "text-emerald-300"}`}>{isSelf ? "你" : message.fromName}：</span>
                          <span className="text-slate-100">{message.text}</span>
                        </div>
                      );
                    })}
                  </div>
                  <ChatInput
                    value={friendInput}
                    onChange={setFriendInput}
                    onSend={sendFriendMessage}
                    disabled={coolingDown || !selectedFriend?.online}
                    placeholder={selectedFriend?.online ? `发给 ${selectedFriend.name}…` : "好友当前离线"}
                  />
                </>
              ) : (
                <div className="flex h-56 items-center justify-center px-6 text-center text-xs leading-5 text-slate-500">
                  暂无好友。可以先在大厅的在线玩家列表或好友中心添加好友。
                </div>
              )}
            </>
          )}
        </div>
      )}

      <div className="pointer-events-auto flex items-center gap-2">
        <button
          type="button"
          onClick={() => {
            setSpectatorPinned(false);
            setOpen((value) => !value);
            if (!open && activeTab === "game") setGameUnread(0);
            if (!open && activeTab === "friends" && selectedFriendAccount) {
              setFriendUnread((previous) => ({ ...previous, [selectedFriendAccount]: 0 }));
            }
          }}
          className="relative flex h-10 w-10 items-center justify-center rounded-full bg-slate-800/90 text-lg shadow-lg ring-1 ring-white/15 hover:bg-slate-700"
          title="聊天"
          aria-label="打开聊天"
        >
          💬
          {!open && totalUnread > 0 && (
            <span className="absolute -right-1 -top-1 flex h-5 min-w-5 items-center justify-center rounded-full bg-rose-500 px-1 text-[10px] font-bold text-white ring-2 ring-slate-900">
              {totalUnread > 9 ? "9+" : totalUnread}
            </span>
          )}
        </button>

        {showSpectatorIndicator && (
          <div className="relative" onMouseEnter={() => setSpectatorHovered(true)} onMouseLeave={() => setSpectatorHovered(false)}>
            {showSpectatorList && (
              <div className="absolute bottom-full left-0 mb-2 w-52 rounded-xl bg-slate-900/95 p-3 text-xs text-white shadow-2xl ring-1 ring-purple-300/25">
                <p className="mb-2 font-bold text-purple-200">{spectatorNames.length} 人正在观战</p>
                <div className="max-h-40 space-y-1 overflow-y-auto">
                  {spectatorNames.map((name, index) => (
                    <div key={`${name}:${index}`} className="truncate rounded-md bg-white/5 px-2 py-1 text-slate-200">{name}</div>
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

function ChatInput({ value, onChange, onSend, disabled, placeholder }: {
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
        onKeyDown={(event) => { if (event.key === "Enter") onSend(); }}
        disabled={disabled}
        maxLength={100}
        placeholder={placeholder}
        className="min-w-0 flex-1 rounded-md bg-slate-800 px-2 py-1.5 text-xs text-white outline-none ring-1 ring-white/10 placeholder:text-slate-500 focus:ring-sky-400 disabled:cursor-not-allowed disabled:opacity-60"
      />
      <button type="button" onClick={onSend} disabled={disabled || !value.trim()} className="rounded-md bg-sky-600 px-3 py-1.5 text-xs font-bold text-white hover:bg-sky-500 disabled:bg-slate-700 disabled:opacity-50">
        发送
      </button>
    </div>
  );
}
