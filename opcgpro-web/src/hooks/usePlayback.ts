"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useGameStore } from "@/store/gameStore";
import type { PlaybackRecord, PlaybackState, PlaybackSpeed, PlaybackStep } from "@/types/playback";
import type { CardData } from "@/types/card";
import { getCard } from "@/data/CardLoader";

const DEFAULT_SPEED: PlaybackSpeed = 1;

/**
 * usePlayback — 回放控制器
 * 按时间轴逐步回放 server 录制的 action 序列
 */
export function usePlayback(record: PlaybackRecord | null) {
  const [state, setState] = useState<PlaybackState>("idle");
  const [speed, setSpeed] = useState<PlaybackSpeed>(DEFAULT_SPEED);
  const [currentTurn, setCurrentTurn] = useState(0);
  const [currentStep, setCurrentStep] = useState(0);

  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const isActive = state === "playing";
  const totalTurns = record?.turns.length ?? 0;
  const totalSteps = record?.turns[currentTurn]?.steps.length ?? 0;

  // 执行单步回放 — 使用 store 方法更新状态
  const executeStep = useCallback((step: PlaybackStep) => {
    const store = useGameStore.getState();
    const payload = step.payload;

    switch (step.action) {
      case "TurnStart":
        store.setCurrentTurn(step.side === "my");
        store.setPhase("Main");
        break;

      case "PlayCard": {
        const cardNumber = payload.cardNumber as string;
        const card = getCard(cardNumber);
        if (card) {
          store.playToField(card);
          const handIdx = store[step.side].hand.cards.findIndex(
            (c: CardData) => c?.number === cardNumber
          );
          if (handIdx >= 0) store.removeFromHand(step.side, handIdx);
          store.setCost(
            step.side,
            store[step.side].cost.active - card.cost,
            store[step.side].cost.rest + card.cost,
            store[step.side].cost.max
          );
        }
        break;
      }

      case "Attack": {
        const attackerIdx = payload.attackerIndex as number;
        if (attackerIdx != null) {
          store.tapCard(step.side, attackerIdx);
        }
        break;
      }

      case "Damage": {
        if (payload.target === "leader") {
          const targetSide = step.side === "my" ? "opponent" : "my";
          store.takeDamage(targetSide, 1);
        }
        break;
      }

      case "KOUnit": {
        const idx = payload.cardIndex as number;
        const koSide = (payload.side as "my" | "opponent") ?? "opponent";
        if (idx != null) store.removeFromField(koSide, idx);
        break;
      }

      case "TurnEnd":
        store.setPhase("End");
        store.setCurrentTurn(!store.currentTurn);
        break;

      case "GameOver":
        store.setGameOver(true);
        break;

      case "DrawCard": {
        const drawnNumber = payload.cardNumber as string;
        if (drawnNumber) {
          const card = getCard(drawnNumber);
          if (card) store.addToHand(step.side, card);
        }
        break;
      }

      case "Block": {
        const blockerIdx = payload.blockerIndex as number;
        if (blockerIdx != null) {
          store.tapCard(step.side, blockerIdx);
        }
        break;
      }
    }

    // 设置动画相关字段
    store.setLastAction(step.action, step.payload);
  }, []);

  // 从零重新播放到指定位置
  const replayFromStart = useCallback((endTurn: number, endStep: number) => {
    const store = useGameStore.getState();
    store.resetGame();
    store.setMode("Playback");
    store.setNames(record?.myName ?? "", record?.opponentName ?? "");
    for (let t = 0; t <= endTurn; t++) {
      const steps = record?.turns[t]?.steps ?? [];
      const limit = t < endTurn ? steps.length : endStep;
      for (let i = 0; i < limit; i++) {
        executeStep(steps[i]);
      }
    }
  }, [record, executeStep]);

  // 推进到下一步
  const advance = useCallback(() => {
    if (!record) return;

    const turnSteps = record.turns[currentTurn]?.steps;
    if (!turnSteps || currentStep >= turnSteps.length) {
      const nextTurn = currentTurn + 1;
      if (nextTurn < record.turns.length) {
        setCurrentTurn(nextTurn);
        setCurrentStep(0);
      } else {
        setState("ended");
      }
      return;
    }

    const step = turnSteps[currentStep];
    if (step) {
      executeStep(step);
      setCurrentStep((s) => s + 1);
    }
  }, [record, currentTurn, currentStep, executeStep]);

  // 定时器驱动回放
  useEffect(() => {
    if (!isActive || !record) return;

    const turnSteps = record.turns[currentTurn]?.steps;
    if (!turnSteps || currentStep >= turnSteps.length) {
      const nextTurn = currentTurn + 1;
      if (nextTurn >= record.turns.length) {
        setState("ended");
        return;
      }
      setCurrentTurn(nextTurn);
      setCurrentStep(0);
      return;
    }

    const current = turnSteps[currentStep];
    const prev = currentStep > 0 ? turnSteps[currentStep - 1] : null;
    const baseDelay = prev ? current.timeOffset - prev.timeOffset : current.timeOffset;
    const delay = Math.max(200, baseDelay / speed);

    timerRef.current = setTimeout(() => {
      advance();
    }, delay);

    return () => {
      if (timerRef.current) clearTimeout(timerRef.current);
    };
  }, [isActive, currentTurn, currentStep, record, speed, advance]);

  // ── 控制器方法 ────────────────────────────────────────────────────────────

  const play = useCallback(() => {
    if (state === "ended") {
      replayFromStart(0, 0);
      setCurrentTurn(0);
      setCurrentStep(0);
    }
    setState("playing");
  }, [state, replayFromStart]);

  const pause = useCallback(() => setState("paused"), []);

  const stepForward = useCallback(() => {
    setState("paused");
    advance();
  }, [advance]);

  const stepBackward = useCallback(() => {
    if (!record || currentStep <= 0) return;
    setState("paused");
    const newStep = currentStep - 1;
    replayFromStart(currentTurn, newStep);
    setCurrentStep(newStep);
  }, [record, currentTurn, currentStep, replayFromStart]);

  const setSpeedAndRestart = useCallback((s: PlaybackSpeed) => setSpeed(s), []);

  return {
    state,
    speed,
    currentTurn,
    currentStep,
    totalTurns,
    totalSteps,
    play,
    pause,
    stepForward,
    stepBackward,
    setSpeed: setSpeedAndRestart,
  };
}
