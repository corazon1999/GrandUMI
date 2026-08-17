"use client";

import { useEffect, useRef, useState } from "react";
import { motion } from "framer-motion";
import { HomeRequest } from "@/net/HomeProtocol";
import { useNetStore } from "@/store/netStore";
import { NetManager } from "@/net/NetManager";
import { eventBus } from "@/net/eventBus";
import { getWebSocketEndpoints } from "@/net/wsEndpoint";
import type { MsgLogin } from "@/types/net";
import {
  clearSessionReplacedNotice,
  getSessionReplacedNotice,
} from "@/net/sessionReplacement";

const STATE_LABEL: Record<string, string> = {
  disconnected: "未连接",
  connecting: "连接中...",
  handshaking: "握手中...",
  connected: "已连接",
  reconnecting: "正在重连...",
  recovering: "正在恢复...",
  failed: "连接失败",
};

const PASSWORD_STORAGE_PREFIX = "grandumi_password:";

function passwordStorageKey(account: string) {
  return `${PASSWORD_STORAGE_PREFIX}${account.trim().toLocaleLowerCase("zh-CN")}`;
}

function loadRememberedPassword(account: string) {
  const normalized = account.trim();
  if (!normalized) return "";
  return localStorage.getItem(passwordStorageKey(normalized)) ?? "";
}

function rememberPassword(account: string, password: string) {
  const normalized = account.trim();
  if (!normalized || !password) return;
  localStorage.setItem(passwordStorageKey(normalized), password);
}

export default function LoginPanel() {
  const [account, setAccount] = useState("");
  const [storedAccount, setStoredAccount] = useState("");
  const [editingAccount, setEditingAccount] = useState(true);
  const [authStep, setAuthStep] = useState<"account" | "password" | "setup">("account");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [pending, setPending] = useState(false);
  const submittedLoginRef = useRef<{ account: string; password?: string } | null>(null);
  const connState = useNetStore((s) => s.connState);
  const error = useNetStore((s) => s.error);
  const canLogin = connState === "connected";
  const accountReady = account.trim().length > 0;
  const isConnecting = ["connecting", "handshaking", "reconnecting", "recovering"].includes(connState);
  const canRetry = connState === "failed" || connState === "disconnected" || connState === "reconnecting";

  useEffect(() => {
    const saved = localStorage.getItem("grandumi_account")?.trim() ?? "";
    setAccount(saved);
    setStoredAccount(saved);
    setEditingAccount(!saved);
    const sessionReplacedNotice = getSessionReplacedNotice();
    if (sessionReplacedNotice) useNetStore.getState().setError(sessionReplacedNotice);
  }, []);

  useEffect(() => {
    const onMessage = (message: { proto: string }) => {
      if (message.proto !== "MsgLogin") return;
      const login = message as MsgLogin;
      setPending(false);
      if (login.result) {
        const submitted = submittedLoginRef.current;
        if (submitted?.password) rememberPassword(submitted.account, submitted.password);
        setConfirmPassword("");
        return;
      }
      if (login.needsPassword) {
        const nextAccount = (login.account || submittedLoginRef.current?.account || "").trim();
        if (nextAccount) setAccount(nextAccount);
        const nextStep = login.needsPasswordSetup ? "setup" : "password";
        setAuthStep(nextStep);
        setPassword(nextStep === "setup" ? "" : loadRememberedPassword(nextAccount));
        setConfirmPassword("");
        setShowPassword(false);
      }
    };
    const onClose = () => setPending(false);
    eventBus.on("message", onMessage);
    eventBus.on("close", onClose);
    return () => {
      eventBus.off("message", onMessage);
      eventBus.off("close", onClose);
    };
  }, []);

  const handleLogin = () => {
    if (!canLogin || pending || !account.trim()) return;
    const store = useNetStore.getState();
    clearSessionReplacedNotice();
    store.setError(null);

    if (authStep !== "account" && !password) {
      store.setError("请输入密码。");
      return;
    }
    if (authStep === "setup") {
      if (password.length < 8 || password.length > 128) {
        store.setError("密码长度需为 8–128 个字符。");
        return;
      }
      if (password !== confirmPassword) {
        store.setError("两次输入的密码不一致。");
        return;
      }
    }

    const normalizedAccount = account.trim();
    const submittedPassword = authStep === "account" ? undefined : password;
    const sent = HomeRequest.login(normalizedAccount, submittedPassword);
    if (sent) {
      submittedLoginRef.current = { account: normalizedAccount, password: submittedPassword };
      setPending(true);
    }
  };

  const handleRetry = () => {
    useNetStore.getState().setError(null);
    if (connState === "reconnecting") NetManager.retryNow(getWebSocketEndpoints());
    else NetManager.connect(getWebSocketEndpoints());
  };

  const startChangingAccount = () => {
    clearSessionReplacedNotice();
    setEditingAccount(true);
    setAuthStep("account");
    setPassword("");
    setConfirmPassword("");
    setShowPassword(false);
    setPending(false);
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
        <p className="mb-4 text-center text-sm text-gray-500">
          One Piece Card Game Online
        </p>

        {authStep === "account" && storedAccount && !editingAccount ? (
          <div className="mb-5 rounded-2xl border border-gray-800 bg-gray-950/70 px-4 py-4 text-center">
            <p className="text-sm text-gray-500">欢迎回来</p>
            <p className="mt-1 truncate text-lg font-bold text-white">{storedAccount}</p>
          </div>
        ) : authStep !== "account" ? (
          <div className="mb-5 space-y-4">
            <div className="flex items-center justify-between rounded-xl border border-gray-800 bg-gray-950/70 px-4 py-3">
              <div className="min-w-0">
                <p className="text-xs text-gray-500">登录账号</p>
                <p className="truncate font-bold text-white">{account}</p>
              </div>
              <button type="button" onClick={startChangingAccount} className="min-h-10 shrink-0 px-3 text-sm text-orange-300 hover:text-orange-200">
                更换账号
              </button>
            </div>

            <div>
              <label htmlFor="login-password" className="mb-2 block text-sm font-medium text-gray-300">
                {authStep === "setup" ? "设置密码" : "密码"}
              </label>
              <div className="relative">
                <input
                  id="login-password"
                  type={showPassword ? "text" : "password"}
                  className="h-12 w-full rounded-xl border border-gray-700 bg-gray-800 px-4 pr-12 text-base text-white outline-none transition-colors placeholder:text-gray-600 focus:border-orange-500 focus-visible:ring-2 focus-visible:ring-orange-500/30"
                  placeholder={authStep === "setup" ? "8–128 个字符" : "请输入密码"}
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                  onKeyDown={(event) => event.key === "Enter" && authStep === "password" && handleLogin()}
                  autoComplete={authStep === "setup" ? "new-password" : "current-password"}
                  maxLength={128}
                  autoFocus
                />
                <button
                  type="button"
                  onClick={() => setShowPassword((visible) => !visible)}
                  aria-label={showPassword ? "隐藏密码" : "显示密码"}
                  aria-pressed={showPassword}
                  className="absolute right-1 top-1/2 flex h-10 w-10 -translate-y-1/2 items-center justify-center rounded-lg text-gray-400 transition-colors hover:bg-gray-700 hover:text-white focus-visible:outline-2 focus-visible:outline-orange-400"
                >
                  {showPassword ? (
                    <svg viewBox="0 0 24 24" aria-hidden="true" className="h-5 w-5 fill-none stroke-current" strokeWidth="1.8">
                      <path d="M3 3l18 18" strokeLinecap="round" />
                      <path d="M10.6 10.7a2 2 0 0 0 2.7 2.7M9.9 4.3A10.8 10.8 0 0 1 12 4c5.5 0 9 5.5 9 8a10.4 10.4 0 0 1-2.1 3.4M6.2 6.2C4.1 7.7 3 10.3 3 12c0 2.5 3.5 8 9 8 1.4 0 2.7-.4 3.8-1" strokeLinecap="round" strokeLinejoin="round" />
                    </svg>
                  ) : (
                    <svg viewBox="0 0 24 24" aria-hidden="true" className="h-5 w-5 fill-none stroke-current" strokeWidth="1.8">
                      <path d="M3 12c0-2.5 3.5-8 9-8s9 5.5 9 8-3.5 8-9 8-9-5.5-9-8Z" strokeLinejoin="round" />
                      <circle cx="12" cy="12" r="2.5" />
                    </svg>
                  )}
                </button>
              </div>
              {authStep === "password" && (
                <p className="mt-2 text-xs leading-5 text-gray-500">密码会保存在当前浏览器，下次刷新自动填入。</p>
              )}
            </div>

            {authStep === "setup" && (
              <div>
                <label htmlFor="login-password-confirm" className="mb-2 block text-sm font-medium text-gray-300">
                  确认密码
                </label>
                <input
                  id="login-password-confirm"
                  type={showPassword ? "text" : "password"}
                  className="h-12 w-full rounded-xl border border-gray-700 bg-gray-800 px-4 text-base text-white outline-none transition-colors placeholder:text-gray-600 focus:border-orange-500 focus-visible:ring-2 focus-visible:ring-orange-500/30"
                  placeholder="再输入一次密码"
                  value={confirmPassword}
                  onChange={(event) => setConfirmPassword(event.target.value)}
                  onKeyDown={(event) => event.key === "Enter" && handleLogin()}
                  autoComplete="new-password"
                  maxLength={128}
                />
                <p className="mt-2 text-sm leading-5 text-gray-500">
                  新账号和尚未设密的旧账号，将从本次起使用该密码登录。
                </p>
              </div>
            )}
          </div>
        ) : (
          <div className="mb-5">
            <label htmlFor="login-account" className="mb-2 block text-sm font-medium text-gray-300">
              玩家账号
            </label>
            <input
              id="login-account"
              className="h-12 w-full rounded-xl border border-gray-700 bg-gray-800 px-4 text-base text-white outline-none transition-colors placeholder:text-gray-600 focus:border-orange-500 focus-visible:ring-2 focus-visible:ring-orange-500/30"
              placeholder="请输入账号"
              value={account}
              onChange={(e) => setAccount(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && handleLogin()}
              autoComplete="username"
              maxLength={32}
            />
            <p className="mt-2 text-sm leading-5 text-gray-500">
              首次使用的账号会在设置密码后自动创建。
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
          disabled={!canLogin || !accountReady || pending}
          aria-busy={isConnecting || pending}
          className="h-12 w-full rounded-xl bg-orange-500 text-base font-bold text-white transition-colors hover:bg-orange-400 active:bg-orange-600 disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-500"
        >
          {canLogin
            ? pending
              ? "正在验证..."
              : authStep === "setup"
                ? "设置密码并登录"
                : authStep === "password"
                  ? "登录"
                  : storedAccount && !editingAccount
                    ? `以 ${storedAccount} 继续`
                    : "继续"
            : STATE_LABEL[connState]}
        </button>

        {authStep === "account" && storedAccount && !editingAccount && (
          <button
            type="button"
            onClick={startChangingAccount}
            className="mt-2 h-11 w-full rounded-xl text-sm font-medium text-gray-400 transition-colors hover:bg-gray-800 hover:text-white"
          >
            更换账号
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
              {connState === "reconnecting" ? "立即换线重试" : "重新连接"}
            </button>
          )}
        </div>
      </motion.div>
    </main>
  );
}
