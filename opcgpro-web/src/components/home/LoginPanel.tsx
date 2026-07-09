"use client";

import { useState } from "react";
import { motion } from "framer-motion";
import { HomeRequest } from "@/net/HomeProtocol";
import { useNetStore } from "@/store/netStore";

const STATE_LABEL: Record<string, string> = {
  disconnected: "未连接",
  connecting: "连接中...",
  handshaking: "握手中...",
  connected: "已连接",
};

export default function LoginPanel() {
  const [account, setAccount] = useState("");
  const connState = useNetStore((s) => s.connState);
  const error = useNetStore((s) => s.error);
  const canLogin = connState === "connected";

  const handleLogin = () => {
    if (!account.trim()) return;
    HomeRequest.login(account.trim());
  };

  return (
    <div className="flex flex-col items-center justify-center h-[100dvh] bg-gray-950">
      <motion.div
        className="bg-gray-900 border border-gray-800 rounded-2xl p-8 w-80 max-w-[90vw] shadow-2xl"
        initial={{ opacity: 0, y: 24 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.4 }}
      >
        <h1 className="text-2xl font-bold text-white text-center mb-1">
          GrandUMI
        </h1>
        <p className="text-gray-500 text-xs text-center mb-6">
          One Piece Card Game Online
        </p>

        <div className="flex flex-col gap-3 mb-4">
          {/* #178 无独立注册流程：输名字点登录即自动建号，placeholder 明示引导 */}
          <input
            className="bg-gray-800 text-white rounded-lg px-4 py-2.5 text-sm outline-none border border-gray-700 focus:border-orange-500 transition-colors disabled:opacity-50"
            placeholder="输入名字即可进入（首次自动创建）"
            value={account}
            onChange={(e) => setAccount(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && handleLogin()}
            disabled={!canLogin}
            autoComplete="username"
          />
          {/* #178 小字提示：账号即昵称，无需单独注册 */}
          <p className="text-gray-500 text-xs text-center">
            无需注册，输入的名字即你的昵称
          </p>
        </div>

        {error && (
          <p className="text-red-400 text-xs text-center mb-3">{error}</p>
        )}

        <button
          onClick={handleLogin}
          disabled={!canLogin}
          className="w-full bg-orange-500 hover:bg-orange-400 disabled:bg-gray-700 disabled:cursor-not-allowed text-white font-bold py-2.5 rounded-lg transition-colors text-sm"
        >
          {canLogin ? "登录" : STATE_LABEL[connState]}
        </button>

        {/* 连接状态指示 */}
        <div className="flex items-center justify-center gap-1.5 mt-4">
          <div
            className={`w-1.5 h-1.5 rounded-full ${
              connState === "connected"
                ? "bg-green-400"
                : connState === "connecting" || connState === "handshaking"
                  ? "bg-yellow-400 animate-pulse"
                  : "bg-red-500"
            }`}
          />
          <span className="text-gray-500 text-xs">{STATE_LABEL[connState]}</span>
        </div>
      </motion.div>
    </div>
  );
}
