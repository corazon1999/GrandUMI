"use client";

import { useEffect, useState } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { useGameStore } from "@/store/gameStore";
import { GameRequest } from "@/net/GameRequest";
import { useServerCountdown } from "@/hooks/useServerCountdown";
import type { HexDefinitionSnapshot, HexTierSnapshot } from "@/types/net";

const RECOVERY_RETRY_INTERVAL_MS = 2500;
const MAX_RECOVERY_ATTEMPTS = 3;

const TIER_STYLES: Record<HexTierSnapshot, {
  label: string;
  border: string;
  surface: string;
  accent: string;
}> = {
  Silver: {
    label: "白银海克斯",
    border: "border-slate-300/60",
    surface: "bg-slate-800/85 hover:bg-slate-700/90",
    accent: "text-slate-100",
  },
  Gold: {
    label: "黄金海克斯",
    border: "border-amber-300/70",
    surface: "bg-amber-950/85 hover:bg-amber-900/90",
    accent: "text-amber-200",
  },
  Rainbow: {
    label: "彩虹海克斯",
    border: "border-fuchsia-300/70",
    surface: "bg-gradient-to-br from-violet-950/90 via-fuchsia-950/90 to-sky-950/90 hover:brightness-125",
    accent: "text-fuchsia-200",
  },
};

function HexCandidateCard({
  hex,
  selected,
  disabled,
  onChoose,
}: {
  hex: HexDefinitionSnapshot;
  selected: boolean;
  disabled: boolean;
  onChoose: () => void;
}) {
  const style = TIER_STYLES[hex.tier];
  return (
    <button
      type="button"
      onClick={onChoose}
      disabled={disabled}
      aria-pressed={selected}
      className={`relative min-h-28 rounded-2xl border-2 px-4 py-3 text-left shadow-xl transition duration-150 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-cyan-200 disabled:cursor-wait ${style.border} ${style.surface} ${selected ? "ring-4 ring-cyan-300/80" : ""} disabled:hover:brightness-100`}
    >
      <span className={`block text-[11px] font-black tracking-[0.14em] ${style.accent}`}>
        {style.label} · #{hex.id}
      </span>
      <span className="mt-1.5 block text-base font-black text-white">{hex.name}</span>
      <span className="mt-1.5 block text-xs font-medium leading-5 text-slate-200">{hex.description}</span>
      {selected && (
        <span className="absolute right-3 top-3 rounded-full bg-cyan-300 px-2 py-1 text-[10px] font-black text-slate-950">
          已锁定
        </span>
      )}
    </button>
  );
}

export default function HexDraftOverlay() {
  const hexState = useGameStore((state) => state.hexState);
  const serverNowUtc = useGameStore((state) => state.serverNowUtc);
  const isPending = useGameStore((state) => state.isPending);
  const isGameOver = useGameStore((state) => state.isGameOver);
  const [recoveryAttempts, setRecoveryAttempts] = useState(0);
  const draft = hexState?.activeDraft ?? null;
  const remainingSeconds = useServerCountdown(
    draft?.deadlineUtc ?? null,
    serverNowUtc,
    Boolean(draft && !isGameOver),
  );
  const timedOut = Boolean(draft && remainingSeconds === 0);

  useEffect(() => {
    if (!timedOut || !draft) {
      setRecoveryAttempts(0);
      return;
    }

    let attempts = 0;
    const requestRecovery = () => {
      attempts += 1;
      setRecoveryAttempts(attempts);
      GameRequest.requestState();
    };

    requestRecovery();
    const timer = window.setInterval(() => {
      if (attempts >= MAX_RECOVERY_ATTEMPTS) {
        window.clearInterval(timer);
        return;
      }
      requestRecovery();
    }, RECOVERY_RETRY_INTERVAL_MS);
    return () => window.clearInterval(timer);
  }, [draft?.roundId, timedOut]);

  if (!hexState || isGameOver || (!draft && !hexState.draftResolving)) return null;

  const tier = draft?.tier ?? "Silver";
  const tierStyle = TIER_STYLES[tier];
  const candidates = draft?.candidates ?? [];
  const locked = draft?.myLocked ?? false;
  const choose = (hexId: number) => {
    if (!draft || locked || useGameStore.getState().isPending) return;
    GameRequest.chooseHex(draft.roundId, hexId);
  };

  return (
    <AnimatePresence>
      <motion.div
        key={draft?.roundId ?? "hex-resolving"}
        role="dialog"
        aria-modal="true"
        aria-labelledby="hex-draft-title"
        className="fixed inset-0 z-[145] flex overflow-y-auto bg-slate-950/90 px-[calc(1rem+var(--layout-safe-left,0px))] py-[calc(1rem+var(--layout-safe-top,0px))] [padding-bottom:calc(1rem+var(--layout-safe-bottom,0px))] [padding-right:calc(1rem+var(--layout-safe-right,0px))]"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
      >
        <div className="m-auto flex w-full max-w-5xl flex-col gap-3 rounded-3xl border border-cyan-300/25 bg-slate-950/95 p-4 shadow-[0_0_60px_rgba(34,211,238,.16)] sm:p-5">
          <header className="text-center">
            <p className={`text-xs font-black tracking-[0.18em] ${tierStyle.accent}`}>{tierStyle.label}</p>
            <h1 id="hex-draft-title" className="mt-1 text-xl font-black text-white sm:text-2xl">
              {draft ? "三选一，强化本局对战" : "海克斯正在结算"}
            </h1>
            {draft && (
              <p className={`mt-1 text-sm font-black tabular-nums ${remainingSeconds <= 10 ? "text-rose-300" : "text-cyan-200"}`}>
                剩余 {remainingSeconds} 秒
              </p>
            )}
            <p className="mt-1 text-xs leading-5 text-slate-400">
              {!draft
                ? "服务器正在按固定顺序结算双方选择…"
                : locked
                  ? draft.opponentLocked ? "双方均已锁定，等待服务器结算…" : "你的选择已锁定，等待对手…"
                  : timedOut
                    ? "时间到，服务器正在从你的候选中自动选择；完成前仍可尝试提交。"
                    : "候选仅你可见；未在时限内选择时，由服务器自动决定。"}
            </p>
          </header>

          {draft && candidates.length > 0 && (
            <div className="grid w-full grid-cols-1 gap-2.5 sm:grid-cols-3" aria-label="海克斯候选">
              {candidates.map((hex) => (
                <HexCandidateCard
                  key={hex.id}
                  hex={hex}
                  selected={draft.mySelectedHexId === hex.id}
                  disabled={locked || isPending}
                  onChoose={() => choose(hex.id)}
                />
              ))}
            </div>
          )}

          {draft && candidates.length === 0 && (
            <div className="flex min-h-28 items-center justify-center rounded-2xl border border-white/10 bg-black/25 text-sm text-slate-300" role="status">
              正在同步你的私密候选…
            </div>
          )}

          {(locked || !draft) && (
            <div className="flex min-h-11 items-center justify-center gap-3 text-sm font-bold text-cyan-100" role="status">
              <span className="h-5 w-5 animate-spin rounded-full border-2 border-cyan-200/30 border-t-cyan-100" />
              {draft ? "等待对手与权威结算" : "正在应用海克斯效果"}
            </div>
          )}

          {timedOut && draft && (
            <div className="flex flex-wrap items-center justify-center gap-2 text-center text-xs text-slate-400">
              <span>已自动同步 {recoveryAttempts} 次</span>
              <button
                type="button"
                onClick={() => GameRequest.requestState()}
                className="min-h-11 rounded-lg border border-white/20 bg-slate-800 px-4 py-2 font-bold text-white hover:bg-slate-700"
              >
                重新同步
              </button>
            </div>
          )}
        </div>
      </motion.div>
    </AnimatePresence>
  );
}
