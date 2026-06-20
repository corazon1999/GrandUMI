"use client";

/**
 * GameLog — 操作日志列表
 *
 * 读取 gameStore.logLines（由 syncFromServer 按 tick 去重累积），
 * 新条目在底部，自动滚动到底。
 */

import { useEffect, useRef } from "react";
import { useGameStore } from "@/store/gameStore";

export default function GameLog() {
  const logLines = useGameStore((s) => s.logLines);
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ block: "end" });
  }, [logLines.length]);

  if (logLines.length === 0) {
    return <p className="mt-2 text-[11px] text-slate-600">暂无操作</p>;
  }

  return (
    <div className="mt-2 flex flex-col gap-1">
      {logLines.map((l) => {
        const isTurnMark = l.text.startsWith("——");
        return (
          <div
            key={l.id}
            className={
              isTurnMark
                ? "py-0.5 text-center text-[11px] font-bold text-amber-300/80"
                : "text-[11px] leading-snug text-slate-300"
            }
          >
            {l.text}
          </div>
        );
      })}
      <div ref={bottomRef} />
    </div>
  );
}
