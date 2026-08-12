"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import FriendConversationPicker from "@/components/chat/FriendConversationPicker";
import { GameRequest } from "@/net/GameRequest";
import { HomeRequest } from "@/net/HomeProtocol";
import { friendAccountKey, useNetStore } from "@/store/netStore";

type ChatTab = "lobby" | "friends";

const FRIEND_CHAT_COOLDOWN_MS = 1300;

export default function ChatPanel({ showHeader = true }: { showHeader?: boolean }) {
  const chatMessages = useNetStore((s) => s.chatMessages);
  const friendChatMessages = useNetStore((s) => s.friendChatMessages);
  const friends = useNetStore((s) => s.friends);
  const myAccount = useNetStore((s) => s.account);
  const playerName = useNetStore((s) => s.playerName);
  const connState = useNetStore((s) => s.connState);
  const friendChatUnreadByAccount = useNetStore((s) => s.friendChatUnreadByAccount);
  const markFriendChatRead = useNetStore((s) => s.markFriendChatRead);
  const [activeTab, setActiveTab] = useState<ChatTab>("lobby");
  const [lobbyInput, setLobbyInput] = useState("");
  const [friendInput, setFriendInput] = useState("");
  const [selectedFriendAccount, setSelectedFriendAccount] = useState("");
  const [friendChatCoolingDown, setFriendChatCoolingDown] = useState(false);
  const bottomRef = useRef<HTMLDivElement>(null);
  const cooldownTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const sortedFriends = useMemo(() => [...friends].sort((a, b) => {
    const onlineOrder = Number(b.online) - Number(a.online);
    return onlineOrder || a.name.localeCompare(b.name, "zh-CN");
  }), [friends]);

  const selectedFriend = useMemo(() => sortedFriends.find(
    (friend) => friendAccountKey(friend.account) === friendAccountKey(selectedFriendAccount),
  ), [selectedFriendAccount, sortedFriends]);

  const selectedFriendMessages = useMemo(() => {
    if (!selectedFriendAccount) return [];
    const selectedKey = friendAccountKey(selectedFriendAccount);
    return friendChatMessages.filter((message) => (
      friendAccountKey(message.fromAccount) === selectedKey || friendAccountKey(message.toAccount) === selectedKey
    ));
  }, [friendChatMessages, selectedFriendAccount]);

  const totalFriendUnread = Object.values(friendChatUnreadByAccount).reduce((total, count) => total + count, 0);

  useEffect(() => {
    HomeRequest.requestFriendList();
    return () => {
      if (cooldownTimer.current) clearTimeout(cooldownTimer.current);
    };
  }, []);

  useEffect(() => {
    if (sortedFriends.length === 0) {
      setSelectedFriendAccount("");
      return;
    }
    if (selectedFriend) return;
    setSelectedFriendAccount((sortedFriends.find((friend) => friend.online) ?? sortedFriends[0]).account);
  }, [selectedFriend, sortedFriends]);

  useEffect(() => {
    if (activeTab !== "friends" || !selectedFriendAccount) return;
    markFriendChatRead(selectedFriendAccount);
  }, [activeTab, friendChatMessages, markFriendChatRead, selectedFriendAccount]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [activeTab, chatMessages, selectedFriendAccount, selectedFriendMessages]);

  const sendLobbyMessage = () => {
    const text = lobbyInput.trim();
    if (!text) return;
    HomeRequest.sendChat(text, playerName);
    setLobbyInput("");
  };

  const sendFriendMessage = () => {
    const text = friendInput.trim();
    if (!text || !selectedFriend?.online || friendChatCoolingDown || connState !== "connected") return;
    GameRequest.sendFriendChat(selectedFriend.account, text);
    setFriendInput("");
    setFriendChatCoolingDown(true);
    if (cooldownTimer.current) clearTimeout(cooldownTimer.current);
    cooldownTimer.current = setTimeout(() => setFriendChatCoolingDown(false), FRIEND_CHAT_COOLDOWN_MS);
  };

  const selectFriend = (account: string) => {
    setSelectedFriendAccount(account);
    setFriendInput("");
    markFriendChatRead(account);
  };

  return (
    <div className="flex h-full min-h-0 flex-col" data-testid="lobby-chat-panel">
      {showHeader && (
        <div className="border-b border-gray-800 px-3 py-3">
          <h3 className="text-sm font-bold text-white">聊天</h3>
        </div>
      )}

      <div className="grid grid-cols-2 gap-1 border-b border-gray-800 bg-gray-950/70 p-1" aria-label="聊天频道">
        <button
          type="button"
          onClick={() => setActiveTab("lobby")}
          aria-pressed={activeTab === "lobby"}
          className={`min-h-10 rounded-lg text-xs font-bold transition-colors ${activeTab === "lobby" ? "bg-orange-500 text-white" : "text-gray-500 hover:bg-gray-800 hover:text-gray-200"}`}
        >
          大厅
        </button>
        <button
          type="button"
          onClick={() => setActiveTab("friends")}
          aria-pressed={activeTab === "friends"}
          className={`relative min-h-10 rounded-lg text-xs font-bold transition-colors ${activeTab === "friends" ? "bg-sky-700 text-white" : "text-gray-500 hover:bg-gray-800 hover:text-gray-200"}`}
        >
          好友
          {totalFriendUnread > 0 && (
            <span className="ml-1 rounded-full bg-red-500 px-1.5 py-0.5 text-[9px] text-white">
              {totalFriendUnread > 9 ? "9+" : totalFriendUnread}
            </span>
          )}
        </button>
      </div>

      {activeTab === "lobby" ? (
        <>
          <div className="flex min-h-0 flex-1 flex-col gap-2 overflow-y-auto px-3 py-3" aria-live="polite">
            {chatMessages.length === 0 && (
              <p className="my-auto text-center text-sm text-gray-600">还没有消息，来打个招呼吧</p>
            )}
            {chatMessages.map((msg, i) => (
              <div key={i} className="text-sm leading-5 @[1024px]:text-xs">
                <span className={msg.Name === playerName ? "text-orange-400" : "text-blue-400"}>
                  {msg.Name || "系统"}
                </span>
                <span className="ml-1 text-gray-300">{msg.Msg}</span>
              </div>
            ))}
            <div ref={bottomRef} />
          </div>

          <ChatInput
            value={lobbyInput}
            onChange={setLobbyInput}
            onSend={sendLobbyMessage}
            disabled={connState !== "connected"}
            placeholder={connState === "connected" ? "输入大厅消息" : "等待服务器连接"}
          />
        </>
      ) : sortedFriends.length > 0 ? (
        <>
          <div className="border-b border-gray-800 p-2">
            <FriendConversationPicker
              friends={sortedFriends}
              selectedAccount={selectedFriendAccount}
              unreadByAccount={friendChatUnreadByAccount}
              onSelect={selectFriend}
            />
          </div>

          <div className="flex min-h-0 flex-1 flex-col overflow-y-auto px-3 py-3" aria-live="polite">
            {selectedFriendMessages.length === 0 && (
              <p className="my-auto text-center text-sm leading-6 text-gray-600">
                {selectedFriend?.online ? "还没有消息，向好友打个招呼吧" : "好友当前离线，上线后即可聊天"}
              </p>
            )}
            {selectedFriendMessages.map((message) => {
              const isSelf = friendAccountKey(message.fromAccount) === friendAccountKey(myAccount);
              return (
                <div key={message.id} className="mb-2 text-sm leading-5 @[1024px]:text-xs">
                  <span className={`font-bold ${isSelf ? "text-sky-300" : "text-emerald-300"}`}>
                    {isSelf ? "你" : message.fromName}：
                  </span>
                  <span className="break-words text-gray-200">{message.text}</span>
                </div>
              );
            })}
            <div ref={bottomRef} />
          </div>

          <ChatInput
            value={friendInput}
            onChange={setFriendInput}
            onSend={sendFriendMessage}
            disabled={!selectedFriend?.online || friendChatCoolingDown || connState !== "connected"}
            placeholder={connState !== "connected" ? "等待服务器连接" : selectedFriend?.online ? `发给 ${selectedFriend.name}` : "好友当前离线"}
          />
        </>
      ) : (
        <div className="flex min-h-0 flex-1 items-center justify-center px-6 text-center text-sm leading-6 text-gray-600">
          暂无好友。可以先在好友中心或在线玩家列表添加好友。
        </div>
      )}
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
    <div className="flex gap-2 border-t border-gray-800 p-2">
      <input
        className="h-12 min-w-0 flex-1 rounded-xl border border-gray-700 bg-gray-800 px-3 text-base text-white outline-none transition-colors placeholder:text-gray-600 focus:border-orange-500 disabled:cursor-not-allowed disabled:opacity-60 @[1024px]:h-10 @[1024px]:text-sm"
        placeholder={placeholder}
        aria-label="聊天消息"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === "Enter" && !disabled) onSend();
        }}
        disabled={disabled}
        maxLength={100}
      />
      <button
        type="button"
        onClick={onSend}
        disabled={disabled || !value.trim()}
        className="h-12 min-w-16 rounded-xl bg-orange-500 px-3 text-sm font-bold text-white transition-colors hover:bg-orange-400 disabled:bg-gray-800 disabled:text-gray-600 @[1024px]:h-10"
      >
        发送
      </button>
    </div>
  );
}
