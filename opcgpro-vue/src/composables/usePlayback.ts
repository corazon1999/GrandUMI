import { ref } from "vue";
import type { PlaybackRecord, PlaybackState, PlaybackSpeed } from "@/types/playback";

/**
 * usePlayback — 回放控制器（M6 真正实现，目前为占位）
 *
 * 新架构下回放数据由服务端 ReplayRecorder 写盘，客户端读取 jsonl 后逐 tick 应用 MsgGameState 快照。
 * 当前阶段只保留接口形状，避免破坏 GamePage。
 */
export function usePlayback(_record: PlaybackRecord | null) {
  const state = ref<PlaybackState>("idle");
  const speed = ref<PlaybackSpeed>(1);

  return {
    state,
    speed,
    setSpeed: (s: PlaybackSpeed) => { speed.value = s; },
    play: () => {},
    pause: () => {},
    stop: () => {},
    seekTo: (_turn: number, _step: number) => {},
    currentTurn: 0,
    currentStep: 0,
    totalTurns: 0,
    totalSteps: 0,
  };
}
