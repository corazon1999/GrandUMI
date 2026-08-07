"use client";

import { useEffect, useRef, useState } from "react";
import { useNetStore } from "@/store/netStore";
import { HomeRequest } from "@/net/HomeProtocol";

export default function ChatPanel({ showHeader = true }: { showHeader?: boolean }) {
  const chatMessages = useNetStore((s) => s.chatMessages);
  const playerName = useNetStore((s) => s.playerName);
  const [input, setInput] = useState("");
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [chatMessages]);

  const send = () => {
    const text = input.trim();
    if (!text) return;
    // C# SendChatMsg(msg)：Name 取自 PlayerPanel.PlayerName.text
    HomeRequest.sendChat(text, playerName);
    setInput("");
  };

  return (
    <div className="flex h-full min-h-0 flex-col">
      {showHeader && (
        <div className="border-b border-gray-800 px-3 py-3">
          <h3 className="text-sm font-bold text-white">大厅聊天</h3>
        </div>
      )}

      <div className="flex min-h-0 flex-1 flex-col gap-2 overflow-y-auto px-3 py-3" aria-live="polite">
        {chatMessages.length === 0 && (
          <p className="my-auto text-center text-sm text-gray-600">还没有消息，来打个招呼吧</p>
        )}
        {chatMessages.map((msg, i) => (
          <div key={i} className="text-sm leading-5 lg:text-xs">
            {/* 字段名与 C# MsgChatMsg.Name / MsgChatMsg.Msg 一致 */}
            <span
              className={
                msg.Name === playerName ? "text-orange-400" : "text-blue-400"
              }
            >
              {msg.Name || "系统"}
            </span>
            <span className="text-gray-300 ml-1">{msg.Msg}</span>
          </div>
        ))}
        <div ref={bottomRef} />
      </div>

      <div className="flex gap-2 border-t border-gray-800 p-2">
        <input
          className="h-12 min-w-0 flex-1 rounded-xl border border-gray-700 bg-gray-800 px-3 text-base text-white outline-none transition-colors placeholder:text-gray-600 focus:border-orange-500 lg:h-10 lg:text-sm"
          placeholder="输入消息"
          aria-label="聊天消息"
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && send()}
          maxLength={100}
        />
        <button
          type="button"
          onClick={send}
          disabled={!input.trim()}
          className="h-12 min-w-16 rounded-xl bg-orange-500 px-3 text-sm font-bold text-white transition-colors hover:bg-orange-400 disabled:bg-gray-800 disabled:text-gray-600 lg:h-10"
        >
          发送
        </button>
      </div>
    </div>
  );
}
