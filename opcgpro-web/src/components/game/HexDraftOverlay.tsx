"use client";

import { useEffect, useState } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { useGameStore } from "@/store/gameStore";
import { GameRequest } from "@/net/GameRequest";
import { useServerCountdown } from "@/hooks/useServerCountdown";
import type { HexDefinitionSnapshot, HexTierSnapshot } from "@/types/net";
import styles from "./HexDraftOverlay.module.css";

const RECOVERY_RETRY_INTERVAL_MS = 2500;
const MAX_RECOVERY_ATTEMPTS = 3;

const TIER_STYLES: Record<HexTierSnapshot, {
  label: string;
  shortLabel: string;
  accent: string;
  frame: string;
}> = {
  Silver: {
    label: "银色海克斯",
    shortLabel: "银",
    accent: "text-slate-100",
    frame: styles.silver,
  },
  Gold: {
    label: "金色海克斯",
    shortLabel: "金",
    accent: "text-amber-200",
    frame: styles.gold,
  },
  Rainbow: {
    label: "彩色海克斯",
    shortLabel: "彩",
    accent: "text-fuchsia-200",
    frame: styles.rainbow,
  },
};

function HexCandidateCard({
  hex,
  index,
  selected,
  disabled,
  refreshAvailable,
  refreshed,
  onChoose,
  onRefresh,
}: {
  hex: HexDefinitionSnapshot;
  index: number;
  selected: boolean;
  disabled: boolean;
  refreshAvailable: boolean;
  refreshed: boolean;
  onChoose: () => void;
  onRefresh: () => void;
}) {
  const style = TIER_STYLES[hex.tier];
  return (
    <article
      data-hex-candidate-frame
      data-hex-tier={hex.tier.toLowerCase()}
      className={`${styles.candidate} ${style.frame} ${selected ? styles.selected : ""}`}
    >
      <button
        type="button"
        onClick={onChoose}
        disabled={disabled}
        aria-pressed={selected}
        aria-label={`选择${style.label}“${hex.name}”：${hex.description}`}
        className={styles.choose}
      >
        <span className={`block text-[11px] font-black tracking-[0.16em] ${style.accent}`}>
          {style.label} · 候选 {index + 1}
        </span>
        <span className="mt-2 block text-base font-black text-white">{hex.name}</span>
        <span className="mt-2 block text-xs font-semibold leading-5 text-slate-100">{hex.description}</span>
      </button>
      {selected && (
        <span className="pointer-events-none absolute right-3 top-3 rounded-full bg-cyan-200 px-2 py-1 text-[10px] font-black text-slate-950">
          已锁定
        </span>
      )}
      {refreshed && !selected && (
        <span className="pointer-events-none absolute right-3 top-3 rounded-full border border-white/25 bg-slate-950/80 px-2 py-1 text-[10px] font-black text-cyan-100">
          已刷新
        </span>
      )}
      {refreshAvailable && (
        <button
          type="button"
          onClick={onRefresh}
          disabled={disabled}
          className={styles.refresh}
          aria-label={`刷新候选 ${index + 1}：${hex.name}`}
        >
          刷新此项
        </button>
      )}
    </article>
  );
}

export default function HexDraftOverlay() {
  const hexState = useGameStore((state) => state.hexState);
  const serverNowUtc = useGameStore((state) => state.serverNowUtc);
  const isPending = useGameStore((state) => state.isPending);
  const isGameOver = useGameStore((state) => state.isGameOver);
  const pendingPrompt = useGameStore((state) => state.pendingPrompt);
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

  if (!hexState
    || isGameOver
    || (!draft && !hexState.draftResolving)
    || (!draft && hexState.draftResolving && pendingPrompt)) return null;

  const tier = draft?.tier ?? "Silver";
  const tierStyle = TIER_STYLES[tier];
  const candidates = draft?.candidates ?? [];
  const locked = draft?.myLocked ?? false;
  const tierSequence = (hexState.tierSequence ?? []).map((item) => TIER_STYLES[item].shortLabel);
  const choose = (hexId: number) => {
    if (!draft || locked || useGameStore.getState().isPending) return;
    GameRequest.chooseHex(draft.roundId, hexId);
  };
  const refresh = (candidateIndex: number, expectedHexId: number) => {
    if (!draft?.refreshAvailable || locked || useGameStore.getState().isPending) return;
    GameRequest.refreshHex(draft.roundId, candidateIndex, expectedHexId);
  };

  return (
    <AnimatePresence>
      <motion.div
        key={draft?.roundId ?? "hex-resolving"}
        role="dialog"
        aria-modal="true"
        aria-labelledby="hex-draft-title"
        data-private-hex-draft
        className="@container fixed inset-0 z-[145] flex overflow-y-auto bg-slate-950/90 px-[calc(1rem+var(--layout-safe-left,0px))] py-[calc(1rem+var(--layout-safe-top,0px))] [padding-bottom:calc(1rem+var(--layout-safe-bottom,0px))] [padding-right:calc(1rem+var(--layout-safe-right,0px))]"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
      >
        <div className="m-auto flex w-full max-w-5xl flex-col gap-3 rounded-3xl border border-cyan-300/25 bg-slate-950/95 p-4 shadow-[0_0_60px_rgba(34,211,238,.16)] @[640px]:p-5">
          <header className="text-center">
            <p className={`text-xs font-black tracking-[0.18em] ${tierStyle.accent}`}>{tierStyle.label}</p>
            <h1 id="hex-draft-title" className="mt-1 text-xl font-black text-white @[640px]:text-2xl">
              {draft ? `你的第 ${draft.ownTurnNumber} 回合开始前：三选一` : "正在应用你的海克斯"}
            </h1>
            {tierSequence.length === 3 && (
              <p className="mt-1 text-[11px] font-bold tracking-[0.14em] text-slate-300" aria-label={`本局共享品质序列：${tierSequence.join("、")}`}>
                本局共享品质序列 · {tierSequence.join(" → ")}
              </p>
            )}
            {draft && (
              <p className={`mt-1 text-sm font-black tabular-nums ${remainingSeconds <= 10 ? "text-rose-300" : "text-cyan-200"}`}>
                剩余 {remainingSeconds} 秒
              </p>
            )}
            <p className="mt-1 text-xs leading-5 text-slate-400">
              {!draft
                ? "服务器正在结算你的选择；另一方不会因本次私密选秀等待选择。"
                : locked
                  ? "你的选择已锁定，正在进行权威结算。"
                  : timedOut
                    ? "时间到，服务器将从刷新后的当前候选中自动选择。"
                    : draft.refreshAvailable
                      ? "候选仅你可见；本轮还可刷新其中一项，刷新后仍需选择。"
                      : "候选仅你可见；本轮刷新已使用，请选择一项。"}
            </p>
          </header>

          {draft && candidates.length > 0 && (
            <div className="grid w-full grid-cols-1 gap-2.5 @[640px]:grid-cols-3" aria-label="海克斯候选">
              {candidates.map((hex, index) => (
                <HexCandidateCard
                  key={hex.id}
                  hex={hex}
                  index={index}
                  selected={draft.mySelectedHexId === hex.id}
                  disabled={locked || isPending}
                  refreshAvailable={draft.refreshAvailable}
                  refreshed={draft.refreshedCandidateIndex === index}
                  onChoose={() => choose(hex.id)}
                  onRefresh={() => refresh(index, hex.id)}
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
            <div className="flex min-h-12 items-center justify-center gap-3 text-sm font-bold text-cyan-100" role="status">
              <span className="h-5 w-5 animate-spin rounded-full border-2 border-cyan-200/30 border-t-cyan-100" />
              正在进行权威结算
            </div>
          )}

          {timedOut && draft && (
            <div className="sticky bottom-[var(--layout-safe-bottom,0px)] flex flex-wrap items-center justify-center gap-2 rounded-xl bg-slate-950/90 p-2 text-center text-xs text-slate-400">
              <span>已自动同步 {recoveryAttempts} 次</span>
              <button
                type="button"
                onClick={() => GameRequest.requestState()}
                className="min-h-12 min-w-12 rounded-lg border border-white/20 bg-slate-800 px-4 py-2 font-bold text-white hover:bg-slate-700"
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
