"use client";

import { useMemo, useState, type ReactNode } from "react";
import { friendAccountKey } from "@/store/netStore";
import type { FriendChatMessage, FriendInfo } from "@/types/net";

interface Props {
  friends: FriendInfo[];
  messages: FriendChatMessage[];
  myAccount: string;
  selectedAccount: string;
  unreadByAccount: Record<string, number>;
  input: string;
  disabled: boolean;
  placeholder: string;
  onInputChange: (value: string) => void;
  onSelect: (account: string) => void;
  onBack: () => void;
  onSend: () => void;
  conversationOpen: boolean;
  bottomRef?: React.RefObject<HTMLDivElement | null>;
  headerActions?: ReactNode;
}

function formatConversationTime(timestamp?: number) {
  if (!timestamp) return "";
  const date = new Date(timestamp);
  const now = new Date();
  if (date.toDateString() === now.toDateString()) {
    return new Intl.DateTimeFormat("zh-CN", { hour: "2-digit", minute: "2-digit", hour12: false }).format(date);
  }
  const yesterday = new Date(now);
  yesterday.setDate(now.getDate() - 1);
  if (date.toDateString() === yesterday.toDateString()) return "昨天";
  return new Intl.DateTimeFormat("zh-CN", { month: "numeric", day: "numeric" }).format(date);
}

function formatMessageTime(timestamp: number) {
  return new Intl.DateTimeFormat("zh-CN", {
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  }).format(new Date(timestamp));
}

function avatarColor(account: string) {
  const palette = ["bg-sky-600", "bg-violet-600", "bg-emerald-600", "bg-amber-600", "bg-rose-600", "bg-cyan-600"];
  const hash = Array.from(account).reduce((total, char) => total + char.charCodeAt(0), 0);
  return palette[hash % palette.length];
}

function FriendAvatar({ friend, size = "large" }: { friend: FriendInfo; size?: "small" | "large" }) {
  return (
    <span
      aria-hidden="true"
      className={`relative flex shrink-0 items-center justify-center rounded-xl font-black text-white shadow-sm ${avatarColor(friend.account)} ${
        size === "large" ? "h-12 w-12 text-lg" : "h-9 w-9 text-sm"
      }`}
    >
      {friend.name.trim().slice(0, 1).toLocaleUpperCase("zh-CN") || "友"}
      <span className={`absolute -bottom-0.5 -right-0.5 rounded-full border-2 border-[#111b21] ${friend.online ? "bg-emerald-400" : "bg-gray-500"} ${
        size === "large" ? "h-3.5 w-3.5" : "h-3 w-3"
      }`} />
    </span>
  );
}

export default function FriendChatView({
  friends,
  messages,
  myAccount,
  selectedAccount,
  unreadByAccount,
  input,
  disabled,
  placeholder,
  onInputChange,
  onSelect,
  onBack,
  onSend,
  conversationOpen,
  bottomRef,
  headerActions,
}: Props) {
  const [search, setSearch] = useState("");
  const myKey = friendAccountKey(myAccount);
  const selectedKey = friendAccountKey(selectedAccount);

  const lastMessageByAccount = useMemo(() => {
    const result = new Map<string, FriendChatMessage>();
    for (const message of messages) {
      const otherAccount = friendAccountKey(message.fromAccount) === myKey ? message.toAccount : message.fromAccount;
      const key = friendAccountKey(otherAccount);
      const previous = result.get(key);
      if (!previous || previous.sentAt <= message.sentAt) result.set(key, message);
    }
    return result;
  }, [messages, myKey]);

  const visibleFriends = useMemo(() => {
    const keyword = search.trim().toLocaleLowerCase("zh-CN");
    return [...friends]
      .filter((friend) => !keyword
        || friend.name.toLocaleLowerCase("zh-CN").includes(keyword)
        || friend.account.toLocaleLowerCase("zh-CN").includes(keyword))
      .sort((a, b) => {
        const aLast = lastMessageByAccount.get(friendAccountKey(a.account))?.sentAt ?? 0;
        const bLast = lastMessageByAccount.get(friendAccountKey(b.account))?.sentAt ?? 0;
        return bLast - aLast || Number(b.online) - Number(a.online) || a.name.localeCompare(b.name, "zh-CN");
      });
  }, [friends, lastMessageByAccount, search]);

  const selectedFriend = friends.find((friend) => friendAccountKey(friend.account) === selectedKey);
  const selectedMessages = selectedAccount
    ? messages.filter((message) => (
      friendAccountKey(message.fromAccount) === selectedKey || friendAccountKey(message.toAccount) === selectedKey
    ))
    : [];

  return (
    <div className="@container flex h-full min-h-0 overflow-hidden bg-[#111b21]" data-testid="friend-chat-view">
      <aside
        className={`${conversationOpen ? "hidden" : "flex"} min-h-0 w-full shrink-0 flex-col border-[#27343b] bg-[#111b21] @[560px]:flex @[560px]:w-64 @[560px]:border-r`}
        aria-label="好友会话列表"
      >
        <div className="flex min-h-14 items-center justify-between px-4">
          <div>
            <h3 className="text-base font-bold text-gray-100">好友消息</h3>
            <p className="text-[11px] text-gray-500">{friends.length} 位好友</p>
          </div>
          <span className="rounded-full bg-[#202c33] px-2 py-1 text-[10px] text-gray-400">仅聊天</span>
        </div>

        <label className="mx-3 mb-2 flex min-h-11 items-center gap-2 rounded-lg bg-[#202c33] px-3 text-gray-400 focus-within:ring-1 focus-within:ring-emerald-500">
          <svg viewBox="0 0 24 24" className="h-4 w-4 shrink-0" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
            <circle cx="11" cy="11" r="7" />
            <path d="m20 20-4-4" />
          </svg>
          <span className="sr-only">搜索好友</span>
          <input
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="搜索好友"
            className="min-w-0 flex-1 bg-transparent text-sm text-gray-100 outline-none placeholder:text-gray-500"
          />
          {search && (
            <button type="button" onClick={() => setSearch("")} aria-label="清空搜索" className="flex h-8 w-8 items-center justify-center rounded-full text-gray-500 hover:bg-white/5 hover:text-gray-200">×</button>
          )}
        </label>

        <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain" role="listbox" aria-label="选择聊天好友">
          {visibleFriends.map((friend) => {
            const accountKey = friendAccountKey(friend.account);
            const lastMessage = lastMessageByAccount.get(accountKey);
            const unread = unreadByAccount[accountKey] ?? 0;
            const selected = accountKey === selectedKey;
            const sentByMe = lastMessage && friendAccountKey(lastMessage.fromAccount) === myKey;
            return (
              <button
                key={friend.account}
                type="button"
                role="option"
                aria-selected={selected}
                aria-label={`${friend.name}，${friend.online ? "在线" : "离线"}${unread ? `，${unread} 条未读消息` : ""}`}
                onClick={() => onSelect(friend.account)}
                className={`flex min-h-[4.5rem] w-full items-center gap-3 px-3 text-left transition-colors focus-visible:outline-2 focus-visible:outline-inset focus-visible:outline-emerald-400 ${
                  selected ? "bg-[#2a3942]" : "hover:bg-[#202c33] active:bg-[#2a3942]"
                }`}
              >
                <FriendAvatar friend={friend} />
                <span className="min-w-0 flex-1 border-b border-[#202c33] py-3">
                  <span className="flex items-center gap-2">
                    <span className={`min-w-0 flex-1 truncate text-sm ${unread ? "font-bold text-white" : "font-medium text-gray-200"}`}>{friend.name}</span>
                    <span className={`shrink-0 text-[10px] ${unread ? "text-emerald-400" : "text-gray-500"}`}>{formatConversationTime(lastMessage?.sentAt)}</span>
                  </span>
                  <span className="mt-1 flex items-center gap-2">
                    <span className={`min-w-0 flex-1 truncate text-xs ${unread ? "text-gray-300" : "text-gray-500"}`}>
                      {lastMessage ? `${sentByMe ? "你：" : ""}${lastMessage.text}` : friend.online ? "在线，可以开始聊天" : "离线"}
                    </span>
                    {unread > 0 && (
                      <span className="flex h-5 min-w-5 shrink-0 items-center justify-center rounded-full bg-emerald-500 px-1 text-[10px] font-black text-[#062e24]">
                        {unread > 99 ? "99+" : unread}
                      </span>
                    )}
                  </span>
                </span>
              </button>
            );
          })}
          {visibleFriends.length === 0 && (
            <div className="px-6 py-12 text-center text-sm leading-6 text-gray-500">
              {search ? "没有找到匹配的好友" : "暂无好友，可以先在好友中心添加好友"}
            </div>
          )}
        </div>
      </aside>

      <section className={`${conversationOpen ? "flex" : "hidden"} min-h-0 min-w-0 flex-1 flex-col bg-[#0b141a] @[560px]:flex`} aria-label={selectedFriend ? `与 ${selectedFriend.name} 聊天` : "好友聊天"}>
        {selectedFriend ? (
          <>
            <header className="flex min-h-14 shrink-0 items-center gap-2 border-b border-[#27343b] bg-[#202c33] px-2 @[560px]:px-4">
              <button type="button" onClick={onBack} aria-label="返回好友会话列表" className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full text-gray-300 hover:bg-white/5 @[560px]:hidden">
                <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true"><path d="m15 18-6-6 6-6" /></svg>
              </button>
              <FriendAvatar friend={selectedFriend} size="small" />
              <div className="min-w-0 flex-1">
                <h3 className="truncate text-sm font-bold text-gray-100">{selectedFriend.name}</h3>
                <p className={`text-[11px] ${selectedFriend.online ? "text-emerald-400" : "text-gray-500"}`}>{selectedFriend.online ? "在线" : "离线，暂时无法发送消息"}</p>
              </div>
              {headerActions && <div className="flex shrink-0 items-center gap-1">{headerActions}</div>}
            </header>

            <div className="friend-chat-wallpaper flex min-h-0 flex-1 flex-col overflow-y-auto overscroll-contain px-3 py-4 @[560px]:px-5" aria-live="polite">
              {selectedMessages.length === 0 && (
                <div className="my-auto text-center">
                  <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-[#202c33] text-gray-500">
                    <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden="true"><path d="M5 18.5 3.8 21l3.8-1.3c1.3.6 2.8.9 4.4.9 5 0 9-3.5 9-8s-4-8-9-8-9 3.5-9 8c0 2.3 1 4.4 2 6Z" /></svg>
                  </div>
                  <p className="mt-3 text-sm text-gray-500">{selectedFriend.online ? "还没有消息，发个招呼吧" : "好友当前离线，上线后即可聊天"}</p>
                </div>
              )}
              {selectedMessages.map((message) => {
                const isSelf = friendAccountKey(message.fromAccount) === myKey;
                return (
                  <div key={message.id} className={`mb-3 flex items-end gap-2 ${isSelf ? "justify-end" : "justify-start"}`}>
                    {!isSelf && <FriendAvatar friend={selectedFriend} size="small" />}
                    <div className={`max-w-[78%] rounded-xl px-3 py-2 shadow-sm ${isSelf ? "rounded-br-sm bg-[#005c4b] text-white" : "rounded-bl-sm bg-[#202c33] text-gray-100"}`}>
                      <p className="break-words whitespace-pre-wrap text-sm leading-5">{message.text}</p>
                      <p className={`mt-1 text-right text-[9px] ${isSelf ? "text-emerald-100/60" : "text-gray-500"}`}>{formatMessageTime(message.sentAt)}</p>
                    </div>
                  </div>
                );
              })}
              <div ref={bottomRef} />
            </div>

            <div className="shrink-0 border-t border-[#27343b] bg-[#202c33] p-2 [padding-bottom:calc(0.5rem+var(--layout-safe-bottom,env(safe-area-inset-bottom)))]">
              <div className="flex items-end gap-2">
                <textarea
                  value={input}
                  onChange={(event) => onInputChange(event.target.value)}
                  onKeyDown={(event) => {
                    if (event.key === "Enter" && !event.shiftKey && !disabled) {
                      event.preventDefault();
                      onSend();
                    }
                  }}
                  disabled={disabled}
                  maxLength={100}
                  rows={1}
                  placeholder={placeholder}
                  aria-label="好友聊天消息"
                  className="min-h-11 max-h-24 min-w-0 flex-1 resize-none rounded-lg bg-[#2a3942] px-3 py-2.5 text-base leading-6 text-white outline-none placeholder:text-gray-500 focus:ring-1 focus:ring-emerald-500 disabled:cursor-not-allowed disabled:opacity-60 @[560px]:text-sm"
                />
                <button
                  type="button"
                  onClick={onSend}
                  disabled={disabled || !input.trim()}
                  aria-label="发送好友消息"
                  className="flex h-11 min-w-14 shrink-0 items-center justify-center rounded-lg bg-emerald-600 px-3 text-sm font-bold text-white transition-colors hover:bg-emerald-500 disabled:bg-[#2a3942] disabled:text-gray-600"
                >
                  发送
                </button>
              </div>
              <p className="mt-1 hidden pl-1 text-[10px] text-gray-500 @[560px]:block">Enter 发送，Shift + Enter 换行</p>
            </div>
          </>
        ) : (
          <div className="hidden h-full items-center justify-center px-6 text-center @[560px]:flex">
            <div>
              <div className="mx-auto flex h-16 w-16 items-center justify-center rounded-2xl bg-[#202c33] text-emerald-500">
                <svg viewBox="0 0 24 24" className="h-8 w-8" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden="true"><path d="M5 18.5 3.8 21l3.8-1.3c1.3.6 2.8.9 4.4.9 5 0 9-3.5 9-8s-4-8-9-8-9 3.5-9 8c0 2.3 1 4.4 2 6Z" /><path d="M8 12h.01M12 12h.01M16 12h.01" /></svg>
              </div>
              <p className="mt-4 text-sm font-medium text-gray-300">选择一位好友开始聊天</p>
              <p className="mt-1 text-xs text-gray-600">消息只会发送给当前好友</p>
            </div>
          </div>
        )}
      </section>
    </div>
  );
}
