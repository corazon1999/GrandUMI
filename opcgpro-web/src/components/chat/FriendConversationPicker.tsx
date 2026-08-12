"use client";

import { friendAccountKey } from "@/store/netStore";
import type { FriendInfo } from "@/types/net";

interface Props {
  friends: FriendInfo[];
  selectedAccount: string;
  unreadByAccount: Record<string, number>;
  onSelect: (account: string) => void;
}

export default function FriendConversationPicker({
  friends,
  selectedAccount,
  unreadByAccount,
  onSelect,
}: Props) {
  const selectedKey = friendAccountKey(selectedAccount);

  return (
    <div
      className="flex gap-2 overflow-x-auto overscroll-x-contain px-2 py-2"
      role="listbox"
      aria-label="选择聊天好友"
    >
      {friends.map((friend) => {
        const accountKey = friendAccountKey(friend.account);
        const unread = unreadByAccount[accountKey] ?? 0;
        const selected = accountKey === selectedKey;
        return (
          <button
            key={friend.account}
            type="button"
            role="option"
            aria-selected={selected}
            aria-label={`${friend.name}，${friend.online ? "在线" : "离线"}${unread ? `，${unread} 条未读消息` : ""}`}
            onClick={() => onSelect(friend.account)}
            className={`relative flex min-h-11 max-w-48 shrink-0 items-center gap-2 rounded-lg border px-3 py-1.5 text-left transition-colors ${
              selected
                ? "border-sky-400 bg-sky-700 text-white"
                : "border-gray-700 bg-gray-900 text-gray-300 hover:border-gray-500 hover:bg-gray-800"
            }`}
          >
            <span
              aria-hidden="true"
              className={`h-2 w-2 shrink-0 rounded-full ${friend.online ? "bg-emerald-400" : "bg-gray-600"}`}
            />
            <span className="min-w-0">
              <span className="block truncate text-xs font-bold">{friend.name}</span>
              <span className={`block truncate text-[10px] ${selected ? "text-sky-100" : "text-gray-500"}`}>
                @{friend.account}
              </span>
            </span>
            {unread > 0 && (
              <span className="flex h-5 min-w-5 shrink-0 items-center justify-center rounded-full bg-red-500 px-1 text-[10px] font-black text-white">
                {unread > 9 ? "9+" : unread}
              </span>
            )}
          </button>
        );
      })}
    </div>
  );
}
