"use client";

import { useNetStore } from "@/store/netStore";

export default function NetStatePanel() {
  const connState = useNetStore((s) => s.connState);
  const isOk = connState === "connected";
  const isPending = connState === "connecting" || connState === "handshaking";

  return (
    <div className="flex items-center gap-1.5 text-xs">
      <div
        className={`w-2 h-2 rounded-full ${
          isOk
            ? "bg-green-400"
            : isPending
              ? "bg-yellow-400 animate-pulse"
              : "bg-red-500 animate-pulse"
        }`}
      />
      <span
        className={
          isOk ? "text-green-400" : isPending ? "text-yellow-400" : "text-red-400"
        }
      >
        {isOk ? "已连接" : isPending ? "连接中" : "未连接"}
      </span>
    </div>
  );
}
