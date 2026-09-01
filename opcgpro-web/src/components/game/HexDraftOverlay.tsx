"use client";

import { useEffect, useRef, useState } from "react";
import { AnimatePresence, motion, useReducedMotion } from "framer-motion";
import { useGameStore } from "@/store/gameStore";
import { GameRequest } from "@/net/GameRequest";
import { useServerCountdown } from "@/hooks/useServerCountdown";
import { useAudio } from "@/hooks/useAudio";
import { useLayoutQuarterTurn } from "@/components/ui/ResponsiveScope";
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
    shortLabel: "银色",
    accent: "text-slate-100",
    frame: styles.silver,
  },
  Gold: {
    label: "金色海克斯",
    shortLabel: "金色",
    accent: "text-amber-200",
    frame: styles.gold,
  },
  Rainbow: {
    label: "棱彩海克斯",
    shortLabel: "棱彩",
    accent: "text-fuchsia-200",
    frame: styles.rainbow,
  },
};

type DraftAudioSnapshot = {
  roundId: string;
  refreshSignature: string;
  locked: boolean;
};

function EyeIcon({ open = true }: { open?: boolean }) {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24" className={styles.actionIcon}>
      <path d="M2.6 12s3.4-5.4 9.4-5.4 9.4 5.4 9.4 5.4-3.4 5.4-9.4 5.4S2.6 12 2.6 12Z" />
      <circle cx="12" cy="12" r="2.65" />
      {!open && <path d="m4 4 16 16" />}
    </svg>
  );
}

function RefreshIcon() {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24" className={styles.refreshIcon}>
      <path d="M19.2 8.6A7.7 7.7 0 0 0 5.7 7.2L3.8 9.1" />
      <path d="M3.8 4.8v4.3h4.3" />
      <path d="M4.8 15.4a7.7 7.7 0 0 0 13.5 1.4l1.9-1.9" />
      <path d="M20.2 19.2v-4.3h-4.3" />
    </svg>
  );
}

function HexSigil({ hexId }: { hexId: number }) {
  const variant = Math.abs(hexId) % 6;
  return (
    <span className={styles.sigil} aria-hidden="true">
      <svg viewBox="0 0 96 96" role="presentation">
        <circle className={styles.sigilOrbit} cx="48" cy="48" r="36" />
        <path className={styles.sigilTick} d="M48 5v12M48 79v12M5 48h12M79 48h12" />
        {variant === 0 && <path d="m48 18 9 21 21 9-21 9-9 21-9-21-21-9 21-9 9-21Z" />}
        {variant === 1 && <path d="m30 67 8-28 26-16-7 27-27 17Zm8-28 19 11M29 69l15-4" />}
        {variant === 2 && <path d="M24 55c10-22 38-25 48-6-10 22-38 25-48 6Zm15-5c6 8 14 9 22 2" />}
        {variant === 3 && <path d="m48 19 23 10v19c0 14-10 23-23 29-13-6-23-15-23-29V29l23-10Zm0 13v31" />}
        {variant === 4 && <path d="M25 62c12-4 16-14 17-36 13 13 19 28 7 43 10-3 16-11 19-24 7 17-2 31-20 33-12 1-22-6-23-16Z" />}
        {variant === 5 && <path d="m48 18 10 17 20 4-14 15 2 21-18-9-18 9 2-21-14-15 20-4 10-17Z" />}
      </svg>
      <span className={styles.sigilCore} />
    </span>
  );
}

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
  const tierLabel = hex.tierLabel ?? style.shortLabel;
  const refreshDisabled = disabled || !refreshAvailable;
  return (
    <div className={styles.candidateSlot} data-hex-candidate-slot={index + 1}>
      <article
        data-hex-candidate-frame
        data-hex-tier={hex.tier.toLowerCase()}
        className={`${styles.candidate} ${style.frame} ${selected ? styles.selected : ""}`}
      >
        <span className={styles.cornerIndex} aria-hidden="true">{index + 1}</span>
        <button
          type="button"
          onClick={onChoose}
          disabled={disabled}
          aria-pressed={selected}
          aria-label={`选择${tierLabel}海克斯“${hex.name}”：${hex.description}`}
          className={styles.choose}
        >
          <HexSigil hexId={hex.id} />
          <span className={styles.name}>{hex.name}</span>
          <span className={styles.tierBadge}>{tierLabel}</span>
          <span className={styles.divider} aria-hidden="true" />
          <span className={styles.description}>{hex.description}</span>
          <span className={styles.chooseHint}>{selected ? "选择已锁定" : "点击卡牌选择"}</span>
        </button>
        {selected && <span className={styles.lockedBadge}>已锁定</span>}
        {refreshed && !selected && <span className={styles.refreshedBadge}>已刷新</span>}
      </article>

      <button
        type="button"
        onClick={onRefresh}
        disabled={refreshDisabled}
        className={styles.refresh}
        data-hex-refresh-state={refreshAvailable ? "available" : "used"}
        aria-label={refreshAvailable
          ? `刷新候选 ${index + 1}：${hex.name}`
          : `本轮刷新机会已使用，候选 ${index + 1} 不可刷新`}
      >
        <RefreshIcon />
        <span>{refreshAvailable ? "刷新此项" : "刷新已使用"}</span>
      </button>
    </div>
  );
}

export default function HexDraftOverlay() {
  const hexState = useGameStore((state) => state.hexState);
  const serverNowUtc = useGameStore((state) => state.serverNowUtc);
  const isPending = useGameStore((state) => state.isPending);
  const isGameOver = useGameStore((state) => state.isGameOver);
  const pendingPrompt = useGameStore((state) => state.pendingPrompt);
  const [recoveryAttempts, setRecoveryAttempts] = useState(0);
  const [isHidden, setIsHidden] = useState(false);
  const previousDraftAudioRef = useRef<DraftAudioSnapshot | null>(null);
  const reopenButtonRef = useRef<HTMLButtonElement | null>(null);
  const dialogRef = useRef<HTMLDivElement | null>(null);
  const reduceMotion = useReducedMotion();
  const rotateQuarterTurn = useLayoutQuarterTurn();
  const { play } = useAudio();
  const draft = hexState?.activeDraft ?? null;
  const remainingSeconds = useServerCountdown(
    draft?.deadlineUtc ?? null,
    serverNowUtc,
    Boolean(draft && !isGameOver),
  );
  const timedOut = Boolean(draft && remainingSeconds === 0);
  const refreshSignature = draft
    ? `${draft.refreshedCandidateIndex ?? "none"}:${(draft.candidates ?? []).map((candidate) => candidate.id).join(",")}`
    : "";

  useEffect(() => {
    if (!draft || isGameOver) {
      previousDraftAudioRef.current = null;
      setIsHidden(false);
      return;
    }

    const previous = previousDraftAudioRef.current;
    if (!previous || previous.roundId !== draft.roundId) {
      setIsHidden(false);
      play("hexDraftOpen");
    } else {
      if (previous.refreshSignature !== refreshSignature
        && draft.refreshedCandidateIndex !== null
        && !isHidden) {
        play("hexDraftRefresh");
      }
      if (!previous.locked && draft.myLocked && !isHidden) {
        play("hexDraftConfirm");
      }
    }

    previousDraftAudioRef.current = {
      roundId: draft.roundId,
      refreshSignature,
      locked: draft.myLocked,
    };
  }, [draft, isGameOver, isHidden, play, refreshSignature]);

  useEffect(() => {
    if (!draft || isHidden) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      event.preventDefault();
      setIsHidden(true);
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [draft, isHidden]);

  useEffect(() => {
    if (!draft) return;
    if (isHidden) reopenButtonRef.current?.focus();
    else dialogRef.current?.focus();
  }, [draft, isHidden]);

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
  const tierLabel = draft?.tierLabel ?? tierStyle.shortLabel;
  const tierHeading = `${tierLabel}海克斯`;
  const candidates = draft?.candidates ?? [];
  const locked = draft?.myLocked ?? false;
  const choose = (hexId: number) => {
    if (!draft || locked || useGameStore.getState().isPending) return;
    GameRequest.chooseHex(draft.roundId, hexId);
  };
  const refresh = (candidateIndex: number, expectedHexId: number) => {
    if (!draft?.refreshAvailable || locked || useGameStore.getState().isPending) return;
    GameRequest.refreshHex(draft.roundId, candidateIndex, expectedHexId);
  };

  return (
    <AnimatePresence mode="wait">
      {isHidden && draft ? (
        <motion.button
          key={`hex-reopen-${draft.roundId}`}
          ref={reopenButtonRef}
          type="button"
          onClick={() => setIsHidden(false)}
          data-private-hex-draft-hidden
          className={`${styles.reopen} ${rotateQuarterTurn ? styles.reopenQuarterTurn : ""} ${tierStyle.frame}`}
          aria-label={`重新打开${tierHeading}选择面板，剩余 ${remainingSeconds} 秒`}
          initial={reduceMotion ? false : { opacity: 0, scale: 0.9, x: 10 }}
          animate={{ opacity: 1, scale: 1, x: 0 }}
          exit={reduceMotion ? { opacity: 0 } : { opacity: 0, scale: 0.92, x: 10 }}
          onAnimationComplete={() => reopenButtonRef.current?.focus()}
        >
          <EyeIcon />
          <span className={styles.reopenCopy}>
            <strong>打开海克斯选择</strong>
            <small>{tierLabel} · {remainingSeconds} 秒</small>
          </span>
        </motion.button>
      ) : (
        <motion.div
          key={draft?.roundId ?? "hex-resolving"}
          ref={dialogRef}
          tabIndex={-1}
          role="dialog"
          aria-modal="true"
          aria-labelledby="hex-draft-title"
          data-private-hex-draft
          className={`${styles.overlay} ${rotateQuarterTurn ? styles.quarterTurn : ""} @container`}
          initial={reduceMotion ? false : { opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          onAnimationComplete={() => dialogRef.current?.focus()}
        >
          <div className={styles.ambient} aria-hidden="true" />
          <motion.div
            className={styles.shell}
            initial={reduceMotion ? false : { opacity: 0, y: 18, scale: 0.985 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            transition={reduceMotion ? { duration: 0 } : { duration: 0.32, ease: "easeOut" }}
          >
            <header className={styles.header}>
              {draft && (
                <button
                  type="button"
                  onClick={() => setIsHidden(true)}
                  className={styles.hide}
                  aria-label="隐藏海克斯选择面板并查看场上局势"
                >
                  <EyeIcon open={false} />
                  <span>查看场上</span>
                  <kbd>Esc</kbd>
                </button>
              )}
              <p className={`${styles.eyebrow} ${tierStyle.accent}`}>{tierHeading}</p>
              <h1 id="hex-draft-title" className={styles.title}>
                {draft ? `第 ${draft.ownTurnNumber} 回合海克斯选择` : "正在应用你的海克斯"}
              </h1>
              {draft && (
                <div className={styles.timerRow}>
                  <span>选择一项强化</span>
                  <span aria-hidden="true" className={styles.timerDivider} />
                  <strong className={remainingSeconds <= 10 ? styles.timerUrgent : ""}>
                    {remainingSeconds} 秒
                  </strong>
                </div>
              )}
              <p className={styles.subtitle}>
                {!draft
                  ? "服务器正在结算你的选择；另一方不会因本次私密选秀等待选择。"
                  : locked
                    ? "你的选择已锁定，正在进行权威结算。"
                    : timedOut
                      ? "时间到，服务器将从刷新后的当前候选中自动选择。"
                      : draft.refreshAvailable
                        ? "候选仅你可见；本轮可刷新其中一项，选择后将公开给双方。"
                        : "候选仅你可见；本轮刷新已使用，请选择一项。"}
              </p>
            </header>

            {draft && candidates.length > 0 && (
              <div className={styles.candidates} aria-label="海克斯候选">
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
              <div className={styles.syncing} role="status">
                正在同步你的私密候选…
              </div>
            )}

            {(locked || !draft) && (
              <div className={styles.resolving} role="status">
                <span className={styles.spinner} />
                正在进行权威结算
              </div>
            )}

            {timedOut && draft && (
              <div className={styles.recovery}>
                <span>已自动同步 {recoveryAttempts} 次</span>
                <button type="button" onClick={() => GameRequest.requestState()}>
                  重新同步
                </button>
              </div>
            )}
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
