"use client";

import { useEffect, useRef, useState } from "react";
import { GameRequest } from "@/net/GameRequest";
import { useGameStore } from "@/store/gameStore";
import { elapsedMillisecondsFromServerSync } from "@/lib/serverCountdown.mjs";

const PRESENCE_CONFIRMATION_TIMEOUT_MS = 5_000;

function monotonicNow(): number {
  return typeof performance === "undefined" ? 0 : performance.now();
}

function formatCountdown(milliseconds: number): string {
  const totalSeconds = Math.max(0, Math.ceil(milliseconds / 1000));
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${String(seconds).padStart(2, "0")}`;
}

/** 服务端确认连续 1 分钟无操作后显示；只在真实玩家视角挂载。 */
export default function InactivityWarningOverlay() {
  const active = useGameStore((s) => s.inactivityActive);
  const warning = useGameStore((s) => s.inactivityWarningActive);
  const lossBase = useGameStore((s) => s.inactivityLossRemainingMs);
  const syncUtc = useGameStore((s) => s.inactivitySyncUtc);
  const serverNowUtc = useGameStore((s) => s.serverNowUtc);
  const isGameOver = useGameStore((s) => s.isGameOver);
  const [anchor, setAnchor] = useState(() => ({ syncUtc, serverNowUtc, receivedAt: monotonicNow() }));
  const [now, setNow] = useState(() => monotonicNow());
  const [submitting, setSubmitting] = useState(false);
  const confirmationTimer = useRef<number | null>(null);
  const visible = active === "my" && warning && !isGameOver;

  useEffect(() => {
    if (confirmationTimer.current !== null) {
      window.clearTimeout(confirmationTimer.current);
      confirmationTimer.current = null;
    }
    const receivedAt = monotonicNow();
    setAnchor({ syncUtc, serverNowUtc, receivedAt });
    setNow(receivedAt);
    setSubmitting(false);
  }, [lossBase, serverNowUtc, syncUtc]);

  useEffect(() => {
    if (!visible) {
      if (confirmationTimer.current !== null) {
        window.clearTimeout(confirmationTimer.current);
        confirmationTimer.current = null;
      }
      setSubmitting(false);
      return;
    }
    setNow(monotonicNow());
    const timer = window.setInterval(() => setNow(monotonicNow()), 250);
    return () => window.clearInterval(timer);
  }, [visible]);

  useEffect(() => () => {
    if (confirmationTimer.current !== null) window.clearTimeout(confirmationTimer.current);
  }, []);

  if (!visible) return null;
  const anchorMatchesSnapshot = anchor.syncUtc === syncUtc && anchor.serverNowUtc === serverNowUtc;
  const elapsed = syncUtc
    ? elapsedMillisecondsFromServerSync(
        syncUtc,
        serverNowUtc,
        anchorMatchesSnapshot ? now - anchor.receivedAt : 0,
      )
    : 0;
  const remaining = Math.max(0, lossBase - elapsed);

  const confirmPresence = () => {
    setSubmitting(true);
    if (!GameRequest.confirmInactivityPresence()) {
      setSubmitting(false);
      return;
    }
    confirmationTimer.current = window.setTimeout(() => {
      confirmationTimer.current = null;
      GameRequest.refreshStateSnapshot();
      setSubmitting(false);
    }, PRESENCE_CONFIRMATION_TIMEOUT_MS);
  };

  return (
    <div
      className="pointer-events-auto fixed inset-0 z-[170] flex items-center justify-center bg-black/65 px-[calc(1rem+var(--layout-safe-left,env(safe-area-inset-left)))] py-[calc(1rem+var(--layout-safe-top,env(safe-area-inset-top)))] [padding-bottom:calc(1rem+var(--layout-safe-bottom,env(safe-area-inset-bottom)))] [padding-right:calc(1rem+var(--layout-safe-right,env(safe-area-inset-right)))] backdrop-blur-sm"
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="inactivity-warning-title"
      aria-describedby="inactivity-warning-description"
    >
      <section className="w-full max-w-md rounded-2xl border border-amber-300/50 bg-slate-950/95 p-5 text-center shadow-2xl shadow-black/60">
        <p className="text-xs font-black tracking-[0.22em] text-amber-300">操作确认</p>
        <h2 id="inactivity-warning-title" className="mt-2 text-xl font-black text-white">
          已连续 1 分钟没有操作
        </h2>
        <p id="inactivity-warning-description" className="mt-2 text-sm leading-6 text-slate-300">
          连续 4 分钟没有任何操作将自动判负；贴咚、撤回或确认继续对局都会把本次无操作计时归零。
        </p>
        <p className="mt-4 font-mono text-4xl font-black tabular-nums text-red-300" aria-live="polite">
          {formatCountdown(remaining)}
        </p>
        <p className="mt-1 text-xs font-bold text-slate-400">距离自动判负</p>
        <button
          type="button"
          onClick={confirmPresence}
          disabled={submitting}
          className="mt-5 min-h-12 min-w-48 rounded-xl bg-sky-500 px-6 py-3 text-base font-black text-white shadow-lg shadow-sky-950/50 transition-colors hover:bg-sky-400 disabled:cursor-wait disabled:opacity-60 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-sky-200"
        >
          {submitting ? "正在确认…" : "我还在，继续对局"}
        </button>
      </section>
    </div>
  );
}
