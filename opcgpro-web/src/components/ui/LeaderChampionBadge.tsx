"use client";

import { useEffect, useMemo, useState } from "react";
import { getCard, loadAllCards } from "@/data/CardLoader";

/** 取角色名的最后一段：波特卡斯·D·艾斯 → 艾斯，多弗朗明哥保持完整。 */
export function leaderChampionName(leaderNumber: string): string {
  const name = getCard(leaderNumber)?.name?.trim();
  if (!name) return leaderNumber;
  const parts = name.split(/[·・.]/).map((part) => part.trim()).filter(Boolean);
  return parts.at(-1) || name;
}

export function LeaderChampionBadge({
  leaderNumber,
  className = "",
}: {
  leaderNumber?: string | null;
  className?: string;
}) {
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    let active = true;
    void loadAllCards().then(() => {
      if (active) setRevision((value) => value + 1);
    }).catch(() => undefined);
    return () => { active = false; };
  }, []);

  const title = useMemo(() => {
    void revision;
    return leaderNumber ? `最强${leaderChampionName(leaderNumber)}` : "";
  }, [leaderNumber, revision]);

  if (!leaderNumber) return null;
  return (
    <span
      className={`inline-flex max-w-full shrink-0 flex-col overflow-hidden rounded-md border border-amber-200/55 bg-[linear-gradient(135deg,rgba(120,53,15,.94),rgba(234,88,12,.78))] px-1.5 py-0.5 leading-none shadow-[0_1px_8px_rgba(251,191,36,.25)] ${className}`}
      title={`${title}（${leaderNumber}）`}
    >
      <span className="truncate text-[10px] font-black tracking-wide text-amber-50">
        <span className="mr-0.5 text-amber-200" aria-hidden="true">✦</span>{title}
      </span>
      <span className="mt-0.5 truncate font-mono text-[8px] font-bold tracking-[0.08em] text-amber-100/80">{leaderNumber}</span>
    </span>
  );
}

export function LeaderChampionBadgeList({
  leaderNumbers,
  maxVisible = 2,
  className = "",
}: {
  leaderNumbers?: string[];
  maxVisible?: number;
  className?: string;
}) {
  const unique = Array.from(new Set((leaderNumbers ?? []).filter(Boolean)));
  if (unique.length === 0) return null;
  const visible = unique.slice(0, maxVisible);
  const remaining = unique.length - visible.length;
  return (
    <span className={`flex min-w-0 flex-wrap items-center gap-1 ${className}`}>
      {visible.map((leaderNumber) => <LeaderChampionBadge key={leaderNumber} leaderNumber={leaderNumber} />)}
      {remaining > 0 && <span className="rounded bg-amber-500/15 px-1.5 py-1 text-[10px] font-bold text-amber-200">+{remaining}</span>}
    </span>
  );
}
