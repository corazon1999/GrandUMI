"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { HomeRequest } from "@/net/HomeProtocol";
import { useNetStore } from "@/store/netStore";

/**
 * 观战入口页（M6）
 *
 * 用户输入房间 ID → 客户端发送 MsgSpectateRoom →
 * 服务端把客户端注册为 spectator，从此每次 BroadcastState 都会向其推送脱敏快照。
 */
export default function SpectatePage() {
  const [roomId, setRoomId] = useState("");
  const router = useRouter();
  const isJoining = useNetStore((s) => s.spectateState === "joining");

  const handleSpectate = () => {
    if (isJoining) return;
    HomeRequest.spectateRoom(roomId);
  };

  return (
    <div className="flex items-center justify-center h-screen bg-gray-950">
      <div className="bg-gray-800 rounded-xl p-8 w-96 shadow-2xl">
        <h1 className="text-white text-2xl font-bold mb-6 text-center">观战</h1>
        <input
          className="w-full bg-gray-700 text-white rounded-lg px-4 py-2 mb-4 outline-none"
          placeholder="输入房间 ID"
          value={roomId}
          disabled={isJoining}
          onChange={(e) => setRoomId(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && handleSpectate()}
        />
        <button
          onClick={handleSpectate}
          disabled={isJoining}
          aria-busy={isJoining}
          className="w-full bg-purple-600 hover:bg-purple-500 disabled:bg-purple-950 disabled:text-purple-300 disabled:cursor-wait text-white font-bold py-2 rounded-lg transition-colors"
        >
          {isJoining ? "正在进入…" : "开始观战"}
        </button>
        <button
          onClick={() => router.push("/home")}
          className="w-full mt-2 bg-gray-700 text-gray-300 py-2 rounded-lg transition-colors hover:text-white"
        >
          返回大厅
        </button>
      </div>
    </div>
  );
}
