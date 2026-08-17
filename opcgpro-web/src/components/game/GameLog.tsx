"use client";

/**
 * GameLog — 操作日志列表
 *
 * 读取 gameStore.logLines（由 syncFromServer 按 tick 去重累积），
 * 新条目在底部，自动滚动到底。
 */

import { useEffect, useRef, useState, type ReactNode } from "react";
import { useGameStore } from "@/store/gameStore";
import { getCard } from "@/data/CardLoader";
import CardZoomOverlay from "@/components/ui/CardZoomOverlay";
import GameOverlayPortal from "@/components/ui/GameOverlayPortal";
import { CARD_BACK_SRC } from "@/lib/sprite";

const CARD_NUMBER_PATTERN = /(?:OP|ST|EB|PRB|P)\d{0,2}-\d{3}/gi;

function renderLogContent(content: string, onOpenCard: (number: string) => void): ReactNode[] {
  const nodes: ReactNode[] = [];
  let cursor = 0;
  for (const match of content.matchAll(CARD_NUMBER_PATTERN)) {
    const index = match.index ?? 0;
    if (index > cursor) nodes.push(content.slice(cursor, index));
    const rawNumber = match[0];
    const number = rawNumber.toUpperCase();
    if (getCard(number)) {
      nodes.push(
        <button
          key={`${index}-${number}`}
          type="button"
          onClick={() => onOpenCard(number)}
          className="rounded px-0.5 font-black text-sky-300 underline decoration-sky-400/45 underline-offset-2 hover:bg-sky-400/15 hover:text-sky-100 focus-visible:outline-2 focus-visible:outline-sky-300"
          aria-label={`查看卡牌 ${number} 大图`}
          title={`查看 ${number} 大图`}
        >
          {rawNumber}
        </button>,
      );
    } else {
      nodes.push(rawNumber);
    }
    cursor = index + rawNumber.length;
  }
  if (cursor < content.length) nodes.push(content.slice(cursor));
  return nodes;
}

export default function GameLog() {
  const logLines = useGameStore((s) => s.logLines);
  const bottomRef = useRef<HTMLDivElement>(null);
  const [zoomCardNumber, setZoomCardNumber] = useState<string | null>(null);
  const zoomCard = zoomCardNumber ? getCard(zoomCardNumber) : undefined;

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ block: "end" });
  }, [logLines.length]);

  if (logLines.length === 0) {
    return <p className="mt-2 text-[11px] text-slate-600">暂无操作</p>;
  }

  return (
    <>
      <div className="mt-2 flex flex-col gap-1">
      {logLines.map((l) => {
        const isTurnMark = l.text.startsWith("——");
        const categoryMatch = l.text.match(/^\[([^\]]+)]\s*/);
        const category = categoryMatch?.[1] ?? "";
        const content = categoryMatch ? l.text.slice(categoryMatch[0].length) : l.text;
        const badgeClass = CATEGORY_COLORS[category] ?? "border-slate-600/70 bg-slate-800/70 text-slate-300";
        return (
          <div
            key={l.id}
            className={
              isTurnMark
                ? "py-0.5 text-center text-[11px] font-bold text-amber-300/80"
                : "flex items-start gap-1.5 rounded-md px-1 py-0.5 text-[11px] leading-snug text-slate-300 hover:bg-white/[0.03]"
            }
          >
            {isTurnMark ? (
              l.text
            ) : (
              <>
                {category && (
                  <span className={`mt-px shrink-0 rounded border px-1 py-px text-[9px] font-bold leading-none ${badgeClass}`}>
                    {category}
                  </span>
                )}
                <span className="min-w-0 break-words">
                  {renderLogContent(content, setZoomCardNumber)}
                </span>
              </>
            )}
          </div>
        );
      })}
      <div ref={bottomRef} />
      </div>
      {zoomCard && (
        <GameOverlayPortal>
          <CardZoomOverlay
            card={zoomCard}
            sprite={zoomCard.sprite ?? CARD_BACK_SRC}
            onClose={() => setZoomCardNumber(null)}
          />
        </GameOverlayPortal>
      )}
    </>
  );
}

const CATEGORY_COLORS: Record<string, string> = {
  出牌: "border-sky-500/40 bg-sky-500/10 text-sky-300",
  启动效果: "border-violet-500/40 bg-violet-500/10 text-violet-300",
  效果选择: "border-fuchsia-500/40 bg-fuchsia-500/10 text-fuchsia-300",
  公开: "border-amber-500/40 bg-amber-500/10 text-amber-300",
  攻击: "border-rose-500/40 bg-rose-500/10 text-rose-300",
  阻挡: "border-emerald-500/40 bg-emerald-500/10 text-emerald-300",
  反击: "border-orange-500/40 bg-orange-500/10 text-orange-300",
  咚: "border-cyan-500/40 bg-cyan-500/10 text-cyan-300",
  结束: "border-red-500/40 bg-red-500/10 text-red-300",
  GM: "border-slate-500/40 bg-slate-500/10 text-slate-300",
};
