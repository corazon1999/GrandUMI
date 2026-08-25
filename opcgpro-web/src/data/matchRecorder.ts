/**
 * matchRecorder.ts — 对局录制器（路线二，浏览器侧）
 *
 * 由 GameProtocol 在每收到一份玩家视角 MsgGameState 时调用 onSnapshot()。
 * 职责：
 *   - 按 tick 去重并每 16 帧异步写入 IndexedDB（重连/Resync 重发同 tick 不重复）
 *   - tick 明显回退 → 视为新对局开始，重置缓冲
 *   - 收到 isGameOver 快照 → 组装 MatchMeta 并连同快照流落盘 IndexedDB（仅落一次）
 *
 * 只记录 viewerKind==="player" 的对局（观战不记）。回放页直接驱动 gameStore，
 * 不经过 eventBus → 不会被本录制器二次记录。
 */

import type { MsgGameState } from "@/types/net";
import {
  appendSnapshotChunk,
  deleteMatch,
  saveMatchMeta,
  type MatchMeta,
} from "./matchHistoryDB";
import { shouldHideDisconnectLoss } from "./matchHistoryPolicy";
import { extractMatchOpeningMeta } from "./matchHistoryOpening";

const SNAPSHOT_CHUNK_SIZE = 16;

interface Session {
  id: string;
  startedAt: number;
  pendingSnapshots: MsgGameState[];
  nextChunkIndex: number;
  snapshotCount: number;
  maxTick: number;
  saved: boolean;
  writeChain: Promise<void>;
}

let current: Session | null = null;

function startSession(gs: MsgGameState): Session {
  return {
    id: `${Date.now()}_${gs.opponent?.name || "opp"}`,
    startedAt: Date.now(),
    pendingSnapshots: [],
    nextChunkIndex: 0,
    snapshotCount: 0,
    maxTick: -1,
    saved: false,
    writeChain: Promise.resolve(),
  };
}

function finalize(s: Session, last: MsgGameState): void {
  if (s.saved) return;
  s.saved = true;
  const meta: MatchMeta = {
    id: s.id,
    startedAt: s.startedAt,
    myName: last.my?.name ?? "",
    opponentName: last.opponent?.name ?? "",
    myLeader: last.my?.leaderNumber ?? "",
    opponentLeader: last.opponent?.leaderNumber ?? "",
    winnerIsMe: last.winnerIsMe ?? false,
    isDraw: last.isDraw ?? false,
    ...extractMatchOpeningMeta(last),
    gameOverReason: last.gameOverReason ?? "",
    turnCount: last.turnCount ?? 0,
    snapshotCount: s.snapshotCount,
  };

  if (shouldHideDisconnectLoss(meta)) {
    // 已异步写入的分块也要删除；同一 writeChain 可保证删除发生在旧写入完成之后。
    s.pendingSnapshots = [];
    enqueueWrite(s, () => deleteMatch(s.id));
    return;
  }

  flushChunk(s);
  enqueueWrite(s, () => saveMatchMeta(meta));
}

function flushChunk(s: Session): void {
  if (s.pendingSnapshots.length === 0) return;
  const snapshots = s.pendingSnapshots;
  s.pendingSnapshots = [];
  const chunkIndex = s.nextChunkIndex++;
  enqueueWrite(s, () => appendSnapshotChunk(s.id, chunkIndex, snapshots));
}

function enqueueWrite(s: Session, write: () => Promise<void>) {
  s.writeChain = s.writeChain
    .catch(() => {})
    .then(write)
    .catch((e) => {
      console.warn("[matchRecorder] 保存对局失败:", e);
    });
}

function discardIncomplete(s: Session) {
  s.pendingSnapshots = [];
  enqueueWrite(s, () => deleteMatch(s.id));
}

export const matchRecorder = {
  onSnapshot(gs: MsgGameState): void {
    if (gs.viewerKind !== "player") return;
    const tick = gs.tick ?? 0;
    if (current?.saved && gs.isGameOver && tick === current.maxTick) return;

    // 新对局判定：尚无会话，或 tick 明显回退（新房间引擎从 0 重新计数）
    if (!current || (current.saved && tick <= current.maxTick) || tick + 1 < current.maxTick) {
      if (current && !current.saved) discardIncomplete(current);
      current = startSession(gs);
    }

    // 重连 Resync 可能重复最后一个 tick；已落盘帧不可原地覆盖，直接忽略等价重复。
    if (tick <= current.maxTick) return;
    current.pendingSnapshots.push(gs);
    current.snapshotCount++;
    current.maxTick = tick;
    if (current.pendingSnapshots.length >= SNAPSHOT_CHUNK_SIZE) flushChunk(current);

    if (gs.isGameOver) {
      finalize(current, gs);
    }
  },

  /** 测试/手动复位（一般无需调用，新对局靠 tick 回退自动识别）。 */
  reset(): void {
    if (current && !current.saved) discardIncomplete(current);
    current = null;
  },
};
