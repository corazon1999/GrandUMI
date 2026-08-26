export type RankSnapshotRequestPhase = "idle" | "loading" | "success" | "error";

export interface RankSnapshotRequestState {
  phase: RankSnapshotRequestPhase;
  requestId: string | null;
  error: string | null;
  retryable: boolean;
  seasonId: string | null;
  snapshotVersion: number | null;
  generatedAtUtc: string | null;
}

export interface IncomingRankSnapshotMetadata {
  requestId?: string;
  seasonId: string;
  snapshotVersion?: number;
  generatedAtUtc?: string;
}

export interface AcceptedRankSnapshot {
  state: RankSnapshotRequestState;
  replacePublicSnapshot: boolean;
}

export interface RankProfileOrderKey {
  seasonId: string;
  games: number;
}

export interface RankSnapshotSeasonTransition {
  state: RankSnapshotRequestState;
  clearPublicSnapshot: boolean;
}

export const RANK_LEADERBOARD_REFRESH_INTERVAL_MS = 10 * 60_000;
// 连续约三轮公共快照未成功才提示陈旧，避免正常刷新间隔被误报为故障。
export const RANK_SNAPSHOT_STALE_AFTER_MS = RANK_LEADERBOARD_REFRESH_INTERVAL_MS * 3;

export const INITIAL_RANK_SNAPSHOT_REQUEST_STATE: RankSnapshotRequestState = {
  phase: "idle",
  requestId: null,
  error: null,
  retryable: true,
  seasonId: null,
  snapshotVersion: null,
  generatedAtUtc: null,
};

export function beginRankSnapshotRequest(
  state: RankSnapshotRequestState,
  requestId: string,
): RankSnapshotRequestState {
  return {
    ...state,
    phase: "loading",
    requestId,
    error: null,
    retryable: true,
  };
}

export function failRankSnapshotRequest(
  state: RankSnapshotRequestState,
  requestId: string | null,
  error: string,
  retryable = true,
): RankSnapshotRequestState {
  // 带请求标识的失败只能结束同一个仍在途的请求。较新请求已成功并清空标识后，
  // 旧请求的迟到错误也必须保持无效，不能把成功状态重新覆盖成错误。
  if (requestId && requestId !== state.requestId) return state;
  return {
    ...state,
    phase: "error",
    requestId: null,
    error,
    retryable,
  };
}

function compareRankSeasonIds(left: string, right: string): number | null {
  const leftMatch = /^S([1-9]\d*)$/.exec(left);
  const rightMatch = /^S([1-9]\d*)$/.exec(right);
  if (!leftMatch || !rightMatch) return null;
  const leftOrdinal = Number(leftMatch[1]);
  const rightOrdinal = Number(rightMatch[1]);
  if (!Number.isSafeInteger(leftOrdinal) || !Number.isSafeInteger(rightOrdinal)) return null;
  return Math.sign(leftOrdinal - rightOrdinal);
}

export function shouldReplaceRankProfile(
  current: RankProfileOrderKey | null,
  incoming: RankProfileOrderKey,
  allowSameSeasonRegression = false,
): boolean {
  if (!current) return true;
  if (current.seasonId === incoming.seasonId) {
    return allowSameSeasonRegression || incoming.games >= current.games;
  }

  const seasonOrder = compareRankSeasonIds(incoming.seasonId, current.seasonId);
  return seasonOrder == null || seasonOrder > 0;
}

export function transitionRankSnapshotSeason(
  state: RankSnapshotRequestState,
  incomingSeasonId: string,
): RankSnapshotSeasonTransition {
  if (state.seasonId === incomingSeasonId) {
    return { state, clearPublicSnapshot: false };
  }

  const seasonOrder = state.seasonId == null
    ? null
    : compareRankSeasonIds(incomingSeasonId, state.seasonId);
  if (seasonOrder != null && seasonOrder < 0) {
    return { state, clearPublicSnapshot: false };
  }

  return {
    clearPublicSnapshot: true,
    state: {
      ...state,
      seasonId: incomingSeasonId,
      snapshotVersion: null,
      generatedAtUtc: null,
    },
  };
}

export function acceptRankSnapshot(
  state: RankSnapshotRequestState,
  incoming: IncomingRankSnapshotMetadata,
): AcceptedRankSnapshot {
  const hasVersion = Number.isSafeInteger(incoming.snapshotVersion) && (incoming.snapshotVersion ?? 0) > 0;
  const sameSeason = state.seasonId === incoming.seasonId;
  const seasonOrder = state.seasonId == null || sameSeason
    ? null
    : compareRankSeasonIds(incoming.seasonId, state.seasonId);
  const incomingOlderSeason = seasonOrder != null && seasonOrder < 0;
  const replacePublicSnapshot = state.seasonId == null
    ? true
    : sameSeason
      ? state.snapshotVersion == null
        || (hasVersion && incoming.snapshotVersion! > state.snapshotVersion)
      : incomingOlderSeason
        ? false
        : seasonOrder != null
          ? seasonOrder > 0
          : state.snapshotVersion == null
            || (hasVersion && incoming.snapshotVersion! > state.snapshotVersion);
  const resolvesCurrentRequest = !incomingOlderSeason && (!incoming.requestId
    || !state.requestId
    || incoming.requestId === state.requestId);

  return {
    replacePublicSnapshot,
    state: {
      ...state,
      ...(resolvesCurrentRequest ? {
        phase: "success" as const,
        requestId: null,
        error: null,
        retryable: true,
      } : {}),
      ...(replacePublicSnapshot ? {
        seasonId: incoming.seasonId,
        snapshotVersion: hasVersion ? incoming.snapshotVersion! : null,
        generatedAtUtc: incoming.generatedAtUtc ?? state.generatedAtUtc,
      } : {}),
    },
  };
}

export function isRankSnapshotStale(
  generatedAtUtc: string | null,
  nowMs = Date.now(),
  staleAfterMs = RANK_SNAPSHOT_STALE_AFTER_MS,
): boolean {
  if (!generatedAtUtc) return false;
  const generatedAtMs = Date.parse(generatedAtUtc);
  return Number.isFinite(generatedAtMs) && nowMs - generatedAtMs > staleAfterMs;
}
