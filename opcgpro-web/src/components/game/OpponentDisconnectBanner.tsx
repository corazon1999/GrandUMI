"use client";

import { useEffect, useState } from "react";
import { eventBus } from "@/net/eventBus";

/**
 * OpponentDisconnectBanner — 对手断线横幅提示
 *
 * 监听 eventBus 的 opponentDisconnected / opponentReconnected 事件，
 * 显示宽限期倒计时；对手重连后自动消失。
 * 断线期间双方操作棋钟暂停，服务端按每名玩家每局累计 120 秒宽限权威判负。
 */
export default function OpponentDisconnectBanner() {
  const [countdown, setCountdown] = useState<number | null>(null);
  const [timedOut, setTimedOut] = useState(false);

  useEffect(() => {
    const onDisconnect = (payload: { gracePeriodSeconds: number }) => {
      setCountdown(payload.gracePeriodSeconds);
      setTimedOut(false);
    };
    const onReconnect = () => {
      setCountdown(null);
      setTimedOut(false);
    };

    eventBus.on("opponentDisconnected" as never, onDisconnect as never);
    eventBus.on("opponentReconnected" as never, onReconnect as never);

    return () => {
      eventBus.off("opponentDisconnected" as never, onDisconnect as never);
      eventBus.off("opponentReconnected" as never, onReconnect as never);
    };
  }, []);

  // 本地只负责展示；到 0 后等待服务端权威终局快照。
  useEffect(() => {
    if (countdown === null) return;
    if (countdown <= 0) { setTimedOut(true); return; }
    const t = setInterval(() => setCountdown((n) => (n !== null && n > 0 ? n - 1 : 0)), 1000);
    return () => clearInterval(t);
  }, [countdown]);

  if (countdown === null) return null;

  if (timedOut) {
    return (
      <div
        className="pointer-events-none absolute left-1/2 z-[70] -translate-x-1/2 rounded-lg bg-red-500/95 px-6 py-2 text-center text-sm font-bold text-white shadow-lg"
        style={{
          top: "calc(1rem + var(--layout-safe-top, env(safe-area-inset-top)))",
          maxWidth: "calc(100% - 2rem)",
        }}
      >
        对手重连宽限已结束，正在结算…
      </div>
    );
  }

  return (
    <div
      className="pointer-events-none absolute left-1/2 z-[70] -translate-x-1/2 animate-pulse rounded-lg bg-yellow-500/95 px-6 py-2 text-center text-sm font-bold text-black shadow-lg"
      style={{
        top: "calc(1rem + var(--layout-safe-top, env(safe-area-inset-top)))",
        maxWidth: "calc(100% - 2rem)",
      }}
    >
      对手已断线，等待重连 {countdown}s
    </div>
  );
}
