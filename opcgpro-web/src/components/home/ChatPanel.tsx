"use client";

import { useEffect, useRef, useState } from "react";
import { useNetStore } from "@/store/netStore";
import { HomeRequest } from "@/net/HomeProtocol";

export default function ChatPanel() {
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
    <div className="flex flex-col h-full">
      <div className="px-3 py-2 border-b border-gray-800">
        <h3 className="text-white text-sm font-bold">聊天</h3>
      </div>

      <div className="flex-1 overflow-y-auto px-3 py-2 flex flex-col gap-1">
        {chatMessages.map((msg, i) => (
          <div key={i} className="text-xs">
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

      <div className="px-2 py-2 border-t border-gray-800 flex gap-1">
        <input
          className="flex-1 bg-gray-800 text-white text-xs rounded px-2 py-1.5 outline-none border border-gray-700 focus:border-orange-500 transition-colors"
          placeholder="发送消息..."
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && send()}
          maxLength={100}
        />
        <button
          onClick={send}
          className="bg-orange-500 hover:bg-orange-400 text-white text-xs font-bold px-2 py-1.5 rounded transition-colors"
        >
          发
        </button>
      </div>
    </div>
  );
}
