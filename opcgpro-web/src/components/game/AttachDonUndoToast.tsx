"use client";

import { useEffect, useState, useSyncExternalStore } from "react";
import {
  cancelPendingAttachDon,
  getPendingAttachDonUndo,
  subscribePendingAttachDonUndo,
} from "@/net/GameRequest";

const getServerSnapshot = () => null;

export default function AttachDonUndoToast() {
  const pending = useSyncExternalStore(
    subscribePendingAttachDonUndo,
    getPendingAttachDonUndo,
    getServerSnapshot,
  );
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    if (!pending) return;
    setNow(Date.now());
    const timer = window.setInterval(() => setNow(Date.now()), 100);
    return () => window.clearInterval(timer);
  }, [pending]);

  if (!pending) return null;
  const seconds = Math.max(0, (pending.expiresAt - now) / 1000).toFixed(1);

  return (
    <div
      role="status"
      className="fixed left-1/2 z-[170] flex -translate-x-1/2 items-center gap-3 rounded-xl border border-amber-300/50 bg-slate-950/95 px-3 py-2 text-xs font-bold text-white shadow-2xl"
      style={{ bottom: "calc(1rem + var(--layout-safe-bottom, env(safe-area-inset-bottom)))" }}
    >
      <span>已赋予 {pending.count} 张咚!!，{seconds} 秒后确认</span>
      <button
        type="button"
        onClick={cancelPendingAttachDon}
        className="min-h-11 min-w-11 rounded-lg bg-amber-400 px-4 text-sm font-black text-slate-950 transition-colors hover:bg-amber-300"
      >
        撤回
      </button>
    </div>
  );
}
