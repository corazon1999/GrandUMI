"use client";

import { useState } from "react";
import type { PlaybackRecord, PlaybackState, PlaybackSpeed } from "@/types/playback";

/**
 * usePlayback — 回放控制器（M6 真正实现，目前为占位）
 *
 * 当前玩家回放由浏览器 IndexedDB 保存对局快照流；服务端只保留统一 MatchLogs，
 * 不再为同一局额外写一份 Replays 文件。此处仍只保留旧接口形状，避免破坏 game/page.tsx。
 */
export function usePlayback(_record: PlaybackRecord | null) {
  const [state] = useState<PlaybackState>("idle");
  const [speed, setSpeed] = useState<PlaybackSpeed>(1);

  return {
    state,
    speed,
    setSpeed,
    play:  () => {},
    pause: () => {},
    stop:  () => {},
    seekTo: (_turn: number, _step: number) => {},
    currentTurn: 0,
    currentStep: 0,
    totalTurns: 0,
    totalSteps: 0,
  };
}
