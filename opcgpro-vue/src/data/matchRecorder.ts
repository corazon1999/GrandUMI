/**
 * matchRecorder.ts — 对局录制器（浏览器侧）
 *
 * 由 GameProtocol 在每收到一份玩家视角 MsgGameState 时调用 onSnapshot()。
 * 职责：
 *   - 按 tick 去重累积当前对局的快照流（重连/Resync 重发同 tick 不重复）
 *   - tick 明显回退 → 视为新对局开始，重置缓冲
 *   - 收到 isGameOver 快照 → 组装 MatchMeta 并连同快照流落盘 IndexedDB（仅落一次）
 *
 * 只记录 viewerKind==="player" 的对局（观战不记）。
 */

import type { MsgGameState } from "@/types/net";
import { saveMatch, type MatchMeta } from "./matchHistoryDB";

interface Session {
  id: string;
  startedAt: number;
  snapshots: Map<number, MsgGameState>;
  maxTick: number;
  saved: boolean;
}

let current: Session | null = null;

function startSession(gs: MsgGameState): Session {
  return {
    id: `${Date.now()}_${gs.opponent?.name || "opp"}`,
    startedAt: Date.now(),
    snapshots: new Map(),
    maxTick: -1,
    saved: false,
  };
}

function finalize(s: Session, last: MsgGameState): void {
  if (s.saved) return;
  s.saved = true;
  const snapshots = [...s.snapshots.entries()]
    .sort((a, b) => a[0] - b[0])
    .map(([, v]) => v);
  const meta: MatchMeta = {
    id: s.id,
    startedAt: s.startedAt,
    myName: last.my?.name ?? "",
    opponentName: last.opponent?.name ?? "",
    myLeader: last.my?.leaderNumber ?? "",
    opponentLeader: last.opponent?.leaderNumber ?? "",
    winnerIsMe: last.winnerIsMe ?? false,
    gameOverReason: last.gameOverReason ?? "",
    turnCount: last.turnCount ?? 0,
    snapshotCount: snapshots.length,
  };
  saveMatch(meta, snapshots).catch((e) => {
    console.warn("[matchRecorder] 保存对局失败:", e);
  });
}

export const matchRecorder = {
  onSnapshot(gs: MsgGameState): void {
    if (gs.viewerKind !== "player") return;
    const tick = gs.tick ?? 0;

    if (!current || tick + 1 < current.maxTick) {
      current = startSession(gs);
    }

    current.snapshots.set(tick, gs);
    if (tick > current.maxTick) current.maxTick = tick;

    if (gs.isGameOver) {
      finalize(current, gs);
    }
  },

  reset(): void {
    current = null;
  },
};
