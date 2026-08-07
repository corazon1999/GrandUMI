"use client";

import { useEffect, useState } from "react";
import { motion } from "framer-motion";
import { HomeRequest } from "@/net/HomeProtocol";
import { useNetStore } from "@/store/netStore";
import { NetManager } from "@/net/NetManager";

const STATE_LABEL: Record<string, string> = {
  disconnected: "未连接",
  connecting: "连接中...",
  handshaking: "握手中...",
  connected: "已连接",
  reconnecting: "正在重连...",
  recovering: "正在恢复...",
  failed: "连接失败",
};

export default function LoginPanel() {
  const [account, setAccount] = useState("");
  const [storedAccount, setStoredAccount] = useState("");
  const [editingAccount, setEditingAccount] = useState(true);
  const connState = useNetStore((s) => s.connState);
  const error = useNetStore((s) => s.error);
  const canLogin = connState === "connected";
  const accountReady = account.trim().length > 0;
  const isConnecting = ["connecting", "handshaking", "reconnecting", "recovering"].includes(connState);
  const canRetry = connState === "failed" || connState === "disconnected";

  useEffect(() => {
    const saved = localStorage.getItem("grandumi_account")?.trim() ?? "";
    setAccount(saved);
    setStoredAccount(saved);
    setEditingAccount(!saved);
  }, []);

  const handleLogin = () => {
    if (!canLogin || !account.trim()) return;
    useNetStore.getState().setError(null);
    HomeRequest.login(account.trim());
  };

  const handleRetry = () => {
    useNetStore.getState().setError(null);
    NetManager.connect(process.env.NEXT_PUBLIC_WS_URL ?? "ws://localhost:8080/ws");
  };

  const startChangingAccount = () => {
    setEditingAccount(true);
    useNetStore.getState().setError(null);
  };

  return (
    <main
      className="flex h-[100dvh] flex-col items-center overflow-y-auto bg-gray-950 px-4 py-8"
      style={{
        paddingTop: "calc(2rem + env(safe-area-inset-top))",
        paddingBottom: "calc(2rem + env(safe-area-inset-bottom))",
      }}
    >
      <motion.div
        className="my-auto w-full max-w-sm rounded-3xl border border-gray-800 bg-gray-900 p-6 shadow-2xl sm:p-8"
        initial={{ opacity: 0, y: 24 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.4 }}
      >
        <div className="mx-auto mb-5 flex h-14 w-14 items-center justify-center rounded-2xl bg-orange-500 text-xl font-black text-white shadow-lg shadow-orange-950/40">
          G
        </div>
        <h1 className="mb-1 text-center text-2xl font-bold text-white">
          GrandUMI
        </h1>
        <p className="mb-7 text-center text-sm text-gray-500">
          One Piece Card Game Online
        </p>

        {storedAccount && !editingAccount ? (
          <div className="mb-5 rounded-2xl border border-gray-800 bg-gray-950/70 px-4 py-4 text-center">
            <p className="text-sm text-gray-500">欢迎回来</p>
            <p className="mt-1 truncate text-lg font-bold text-white">{storedAccount}</p>
          </div>
        ) : (
          <div className="mb-5">
            <label htmlFor="login-account" className="mb-2 block text-sm font-medium text-gray-300">
              玩家昵称
            </label>
            <input
              id="login-account"
              className="h-12 w-full rounded-xl border border-gray-700 bg-gray-800 px-4 text-base text-white outline-none transition-colors placeholder:text-gray-600 focus:border-orange-500 focus-visible:ring-2 focus-visible:ring-orange-500/30"
              placeholder="请输入昵称"
              value={account}
              onChange={(e) => setAccount(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && handleLogin()}
              autoComplete="username"
              maxLength={16}
            />
            <p className="mt-2 text-sm leading-5 text-gray-500">
              无需注册，首次进入会自动创建玩家资料。
            </p>
          </div>
        )}

        {error && (
          <p
            role="alert"
            className="mb-4 rounded-xl border border-red-900/70 bg-red-950/40 px-3 py-2.5 text-sm leading-5 text-red-300"
          >
            {error}
          </p>
        )}

        <button
          onClick={handleLogin}
          disabled={!canLogin || !accountReady}
          aria-busy={isConnecting}
          className="h-12 w-full rounded-xl bg-orange-500 text-base font-bold text-white transition-colors hover:bg-orange-400 active:bg-orange-600 disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-500"
        >
          {canLogin
            ? storedAccount && !editingAccount
              ? `以 ${storedAccount} 继续`
              : "进入 GrandUMI"
            : STATE_LABEL[connState]}
        </button>

        {storedAccount && !editingAccount && (
          <button
            type="button"
            onClick={startChangingAccount}
            className="mt-2 h-11 w-full rounded-xl text-sm font-medium text-gray-400 transition-colors hover:bg-gray-800 hover:text-white"
          >
            更换昵称
          </button>
        )}

        <div className="mt-4 flex min-h-11 items-center justify-center gap-2" aria-live="polite">
          <span
            className={`h-2 w-2 rounded-full ${
              connState === "connected"
                ? "bg-green-400"
                : isConnecting
                  ? "bg-yellow-400 animate-pulse"
                  : "bg-red-500"
            }`}
          />
          <span className="text-sm text-gray-500">{STATE_LABEL[connState]}</span>
          {canRetry && (
            <button
              type="button"
              onClick={handleRetry}
              className="ml-1 min-h-11 rounded-lg px-3 text-sm font-medium text-orange-300 hover:bg-gray-800 hover:text-orange-200"
            >
              重新连接
            </button>
          )}
        </div>
      </motion.div>
    </main>
  );
}
