"use client";

import { useEffect, useRef, useSyncExternalStore } from "react";
import {
  cancelPendingAttachDonConfirmation,
  confirmPendingAttachDon,
  getPendingAttachDonConfirmation,
  subscribePendingAttachDonConfirmation,
} from "@/net/GameRequest";

const getServerSnapshot = () => null;

export default function AttachDonConfirmDialog() {
  const pending = useSyncExternalStore(
    subscribePendingAttachDonConfirmation,
    getPendingAttachDonConfirmation,
    getServerSnapshot,
  );
  const confirmButtonRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!pending) return;
    confirmButtonRef.current?.focus();
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") cancelPendingAttachDonConfirmation();
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [pending]);

  if (!pending) return null;

  return (
    <div
      className="pointer-events-auto fixed inset-0 z-[180] flex items-center justify-center bg-black/55 px-[calc(1rem+var(--layout-safe-left,env(safe-area-inset-left)))] py-[calc(1rem+var(--layout-safe-top,env(safe-area-inset-top)))] [padding-bottom:calc(1rem+var(--layout-safe-bottom,env(safe-area-inset-bottom)))] [padding-right:calc(1rem+var(--layout-safe-right,env(safe-area-inset-right)))]"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) cancelPendingAttachDonConfirmation();
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="attach-don-confirm-title"
        aria-describedby="attach-don-confirm-description"
        className="w-full max-w-xs rounded-xl border border-amber-300/45 bg-slate-950/95 p-4 text-white shadow-2xl backdrop-blur"
      >
        <p id="attach-don-confirm-title" className="text-center text-base font-black text-amber-50">
          确认贴{pending.count}咚？
        </p>
        <p
          id="attach-don-confirm-description"
          className="mt-2 text-center text-sm font-medium leading-5 text-slate-300"
        >
          确认后会立即提交并生效；若尚未执行其他对局操作，可在操作区撤回。
        </p>
        <div className="mt-4 grid grid-cols-2 gap-3">
          <button
            type="button"
            onClick={cancelPendingAttachDonConfirmation}
            className="min-h-12 rounded-lg bg-slate-700 px-4 text-sm font-bold text-white transition-colors hover:bg-slate-600 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white"
          >
            取消
          </button>
          <button
            ref={confirmButtonRef}
            type="button"
            onClick={confirmPendingAttachDon}
            className="min-h-12 rounded-lg bg-amber-400 px-4 text-sm font-black text-slate-950 transition-colors hover:bg-amber-300 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-amber-200"
          >
            确认
          </button>
        </div>
      </div>
    </div>
  );
}
