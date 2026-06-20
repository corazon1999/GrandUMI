"use client";

import { useEffect, useState } from "react";
import { eventBus } from "@/net/eventBus";
import { GameRequest } from "@/net/GameRequest";

/**
 * OpponentDisconnectBanner — 对手断线横幅提示
 *
 * 监听 eventBus 的 opponentDisconnected / opponentReconnected 事件，
 * 显示宽限期倒计时；对手重连后自动消失。
 * 倒计时结束后不再静默消失，而是弹窗询问是否结束对局——给一个不依赖后端
 * 90s 计时器的主动出口（后端 fire-and-forget 计时器在服务端重启/异常时会丢失，
 * 否则在线方会永久卡在牌桌无法退回主页）。
 */
export default function OpponentDisconnectBanner() {
  const [countdown, setCountdown] = useState<number | null>(null);
  const [graceTotal, setGraceTotal] = useState(90);
  const [timedOut, setTimedOut] = useState(false);

  useEffect(() => {
    const onDisconnect = (payload: { gracePeriodSeconds: number }) => {
      setGraceTotal(payload.gracePeriodSeconds);
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

  // 本地秒级倒计时；到 0 不再隐藏，而是进入「超时待确认」状态弹窗给出主动出口
  useEffect(() => {
    if (countdown === null) return;
    if (countdown <= 0) { setTimedOut(true); return; }
    const t = setInterval(() => setCountdown((n) => (n !== null && n > 0 ? n - 1 : 0)), 1000);
    return () => clearInterval(t);
  }, [countdown]);

  if (countdown === null) return null;

  // 倒数结束：弹窗询问是否结束对局
  if (timedOut) {
    return (
      <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60">
        <div className="w-80 rounded-xl border border-gray-700 bg-gray-900 p-5 shadow-2xl">
          <p className="mb-1 text-center text-sm text-white">对手仍未重连</p>
          <p className="mb-4 text-center text-xs text-gray-400">
            对手已断线超过 {graceTotal}s 仍未回来，是否结束对局并返回大厅？
          </p>
          <div className="flex gap-2">
            <button
              onClick={() => { setTimedOut(false); setCountdown(30); }}
              className="flex-1 rounded-lg bg-gray-800 py-2 text-xs text-gray-300 transition-colors hover:bg-gray-700"
            >
              再等一会儿
            </button>
            <button
              onClick={() => { GameRequest.endByDisconnect(); setCountdown(null); }}
              className="flex-1 rounded-lg bg-orange-600 py-2 text-xs font-bold text-white transition-colors hover:bg-orange-500"
            >
              结束对局
            </button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="absolute top-4 left-1/2 -translate-x-1/2 z-40 bg-yellow-500/90 text-black px-6 py-2 rounded-lg font-bold shadow-lg text-sm animate-pulse">
      对手已断线，等待重连 {countdown}s
    </div>
  );
}
