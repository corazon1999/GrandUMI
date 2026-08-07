"use client";

import { useEffect, useRef } from "react";
import { useAudio } from "./useAudio";
import { useGameStore } from "@/store/gameStore";
import { useNetStore } from "@/store/netStore";
import type { PlayerSnapshot } from "@/types/net";

interface AudioSnapshot {
  tick: number;
  myLife: number;
  opponentLife: number;
  myFieldIds: Set<string>;
  opponentFieldIds: Set<string>;
  promptId: string | null;
  isGameOver: boolean;
}

function snapshotPlayer(player: PlayerSnapshot | null): Pick<AudioSnapshot, "myLife" | "myFieldIds"> {
  return {
    myLife: player?.lifeCount ?? 0,
    myFieldIds: new Set(player?.fieldCards.map((card) => card.id) ?? []),
  };
}

function hasRemovedCard(previous: Set<string>, current: Set<string>): boolean {
  for (const id of previous) {
    if (!current.has(id)) return true;
  }
  return false;
}

export function useGameAudio(): void {
  const { play, stopAll } = useAudio();
  const tick = useGameStore((state) => state.tick);
  const mode = useGameStore((state) => state.mode);
  const lastAction = useGameStore((state) => state.lastAction);
  const actionPayload = useGameStore((state) => state.lastActionPayloadObj);
  const currentTurn = useGameStore((state) => state.currentTurn);
  const my = useGameStore((state) => state.my);
  const opponent = useGameStore((state) => state.opponent);
  const pendingPrompt = useGameStore((state) => state.pendingPrompt);
  const isGameOver = useGameStore((state) => state.isGameOver);
  const winnerIsMe = useGameStore((state) => state.winnerIsMe);
  const connectionState = useNetStore((state) => state.connState);
  const previousRef = useRef<AudioSnapshot | null>(null);

  useEffect(() => {
    const mySnapshot = snapshotPlayer(my);
    const opponentSnapshot = snapshotPlayer(opponent);
    const current: AudioSnapshot = {
      tick,
      myLife: mySnapshot.myLife,
      opponentLife: opponentSnapshot.myLife,
      myFieldIds: mySnapshot.myFieldIds,
      opponentFieldIds: opponentSnapshot.myFieldIds,
      promptId: pendingPrompt?.promptId ?? null,
      isGameOver,
    };

    if (tick <= 0 || !my || !opponent) {
      previousRef.current = null;
      return;
    }

    if (mode !== "Playback" && connectionState !== "connected") {
      previousRef.current = null;
      return;
    }

    const previous = previousRef.current;
    previousRef.current = current;

    // 首份快照、重连恢复快照、回放倒退或同一 Tick 的本地更新只建立基线。
    if (!previous || tick <= previous.tick) return;

    switch (lastAction) {
      case "MulliganComplete":
        play("matchStart");
        break;
      case "EndTurn":
        play(currentTurn ? "turnSelf" : "turnOpponent");
        break;
      case "PlayCard": {
        const kind = String(actionPayload?.kind ?? "").toLowerCase();
        if (kind === "event") play("cardPlayEvent");
        else if (kind === "stage") play("cardPlayStage");
        else play("cardPlayCharacter");
        break;
      }
      case "AttachDon":
        play("attachDon");
        break;
      case "Attack":
        play("attack");
        break;
      case "DeclareBlocker":
        play("block");
        break;
      case "CounterIcon":
        play("counter");
        break;
      case "UseEffect":
      case "EffectResolved":
        play("effect");
        break;
      case "RevealCards":
        play("reveal");
        break;
      default:
        break;
    }

    if (current.myLife < previous.myLife || current.opponentLife < previous.opponentLife) {
      play("damage");
    }

    if (
      hasRemovedCard(previous.myFieldIds, current.myFieldIds) ||
      hasRemovedCard(previous.opponentFieldIds, current.opponentFieldIds)
    ) {
      play("ko");
    }

    if (current.promptId && current.promptId !== previous.promptId) play("prompt");

    if (current.isGameOver && !previous.isGameOver) {
      stopAll();
      play(winnerIsMe ? "win" : "lose");
    }
  }, [
    actionPayload,
    connectionState,
    currentTurn,
    isGameOver,
    lastAction,
    mode,
    my,
    opponent,
    pendingPrompt,
    play,
    stopAll,
    tick,
    winnerIsMe,
  ]);
}
