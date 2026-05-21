"use client";

import { useEffect, useState } from "react";
import { eventBus } from "@/net/eventBus";

/**
 * OpponentDisconnectBanner — 对手断线横幅提示
 * 对应重构方案 §5.8.7
 *
 * 监听 eventBus 的 opponentDisconnected / opponentReconnected 事件
 * 显示宽限期倒计时，对手重连后自动消失
 */
export default function OpponentDisconnectBanner() {
  const [countdown, setCountdown] = useState<number | null>(null);

  useEffect(() => {
    const onDisconnect = (payload: { gracePeriodSeconds: number }) => {
      setCountdown(payload.gracePeriodSeconds);
    };
    const onReconnect = () => {
      setCountdown(null);
    };

    eventBus.on("opponentDisconnected" as never, onDisconnect as never);
    eventBus.on("opponentReconnected" as never, onReconnect as never);

    return () => {
      eventBus.off("opponentDisconnected" as never, onDisconnect as never);
      eventBus.off("opponentReconnected" as never, onReconnect as never);
    };
  }, []);

  // 本地秒级倒计时
  useEffect(() => {
    if (countdown === null || countdown <= 0) return;
    const t = setInterval(() => setCountdown((n) => (n !== null && n > 0 ? n - 1 : null)), 1000);
    return () => clearInterval(t);
  }, [countdown]);

  if (countdown === null) return null;

  return (
    <div className="absolute top-4 left-1/2 -translate-x-1/2 z-40 bg-yellow-500/90 text-black px-6 py-2 rounded-lg font-bold shadow-lg text-sm animate-pulse">
      对手已断线，等待重连 {countdown}s
    </div>
  );
}
