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
      <div className="absolute left-1/2 top-4 z-40 -translate-x-1/2 rounded-lg bg-red-500/90 px-6 py-2 text-sm font-bold text-white shadow-lg">
        对手重连宽限已结束，正在结算…
      </div>
    );
  }

  return (
    <div className="absolute top-4 left-1/2 -translate-x-1/2 z-40 bg-yellow-500/90 text-black px-6 py-2 rounded-lg font-bold shadow-lg text-sm animate-pulse">
      对手已断线，等待重连 {countdown}s
    </div>
  );
}
