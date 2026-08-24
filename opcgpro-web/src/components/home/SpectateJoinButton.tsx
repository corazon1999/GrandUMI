"use client";

import { useState } from "react";
import { createPortal } from "react-dom";
import { HomeRequest } from "@/net/HomeProtocol";
import { useNetStore } from "@/store/netStore";
import type { SpectateMode } from "@/types/net";

export default function SpectateJoinButton({
  roomId,
  seatIndex,
  mode = "open",
  isFriend = false,
  iconOnly = false,
}: {
  roomId: string;
  seatIndex: 0 | 1;
  mode?: SpectateMode | null;
  isFriend?: boolean;
  iconOnly?: boolean;
}) {
  const spectateState = useNetStore((state) => state.spectateState);
  const spectateRoomId = useNetStore((state) => state.spectateRoomId);
  const [showCode, setShowCode] = useState(false);
  const [code, setCode] = useState("");
  const normalizedMode = mode ?? "open";
  const blocked = normalizedMode === "closed" || (normalizedMode === "friends" && !isFriend);
  const joining = spectateState === "joining" && spectateRoomId === roomId;

  const enter = (spectateCode?: string) => {
    if (normalizedMode === "password" && !spectateCode) {
      setShowCode(true);
      return;
    }
    if (HomeRequest.spectateRoom(roomId, seatIndex, spectateCode)) setShowCode(false);
  };

  const label = joining
    ? "进入中…"
    : normalizedMode === "closed"
      ? "不可观战"
      : normalizedMode === "friends" && !isFriend
        ? "仅限好友"
        : normalizedMode === "password"
          ? "密码观战"
          : "观战";

  return (
    <>
      <button
        type="button"
        onClick={() => enter()}
        disabled={blocked || spectateState === "joining"}
        className={iconOnly
          ? "flex h-11 w-11 min-h-11 min-w-11 items-center justify-center rounded-lg bg-purple-600 p-0 text-white transition-colors hover:bg-purple-500 disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-500"
          : "min-h-11 rounded-lg bg-purple-600 px-3 text-xs font-bold text-white transition-colors hover:bg-purple-500 disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-500"}
        aria-label={label}
        title={label}
      >
        {iconOnly ? (
          <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
            <path d="M2.5 12s3.5-6 9.5-6 9.5 6 9.5 6-3.5 6-9.5 6-9.5-6-9.5-6Z" strokeLinejoin="round" />
            <circle cx="12" cy="12" r="2.5" />
          </svg>
        ) : label}
      </button>
      {showCode && typeof document !== "undefined" && createPortal(
        <div className="fixed inset-0 z-[80] flex items-end justify-center bg-black/70 px-0 pt-[env(safe-area-inset-top)] sm:items-center sm:p-4" role="presentation" onMouseDown={() => setShowCode(false)}>
          <div className="w-full max-w-sm rounded-t-2xl border border-b-0 border-gray-700 bg-gray-900 p-5 pb-[calc(1.25rem+env(safe-area-inset-bottom))] shadow-2xl sm:rounded-2xl sm:border-b sm:p-6" role="dialog" aria-modal="true" aria-labelledby="spectate-code-title" onMouseDown={(event) => event.stopPropagation()}>
            <h2 id="spectate-code-title" className="text-lg font-black text-white">输入观战码</h2>
            <p className="mt-1 text-sm text-gray-500">观战码由主视角玩家在主页生成。</p>
            <input
              autoFocus
              inputMode="numeric"
              autoComplete="one-time-code"
              value={code}
              onChange={(event) => setCode(event.target.value.replace(/\D/g, "").slice(0, 6))}
              onKeyDown={(event) => { if (event.key === "Enter" && code.length === 6) enter(code); }}
              aria-label="六位观战码"
              placeholder="000000"
              className="mt-4 h-12 w-full rounded-xl border border-purple-700 bg-gray-950 px-3 text-center font-mono text-xl font-black tracking-[0.28em] text-white outline-none focus:border-purple-400"
            />
            <div className="mt-4 grid grid-cols-2 gap-3">
              <button type="button" onClick={() => setShowCode(false)} className="min-h-11 rounded-xl bg-gray-800 text-sm font-bold text-gray-300 hover:bg-gray-700">取消</button>
              <button type="button" onClick={() => enter(code)} disabled={code.length !== 6 || spectateState === "joining"} className="min-h-11 rounded-xl bg-purple-600 text-sm font-bold text-white hover:bg-purple-500 disabled:bg-gray-800 disabled:text-gray-600">进入观战</button>
            </div>
          </div>
        </div>,
        document.body,
      )}
    </>
  );
}
