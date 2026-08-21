/**
 * 使用服务端时间锚点计算权威倒计时，避免玩家设备时钟快慢导致提前超时。
 * elapsedMs 必须来自 performance.now() 等单调时钟。
 *
 * @param {string | null | undefined} deadlineUtc
 * @param {string | null | undefined} serverNowUtc
 * @param {number} elapsedMs
 * @param {number} fallbackSeconds
 */
export function remainingSecondsFromServer(
  deadlineUtc,
  serverNowUtc,
  elapsedMs,
  fallbackSeconds = 60,
) {
  const deadlineMs = deadlineUtc ? Date.parse(deadlineUtc) : Number.NaN;
  if (!Number.isFinite(deadlineMs)) return fallbackSeconds;

  const serverNowMs = serverNowUtc ? Date.parse(serverNowUtc) : Number.NaN;
  const estimatedNowMs = Number.isFinite(serverNowMs)
    ? serverNowMs + Math.max(0, elapsedMs)
    : Date.now();
  return Math.max(0, Math.ceil((deadlineMs - estimatedNowMs) / 1000));
}

/**
 * 计算服务端棋钟快照生成后已经流逝的毫秒数。
 * 两个 UTC 字段都来自服务端，只用它们计算快照内的时间差；快照抵达后改用单调时钟，
 * 避免玩家设备时间快慢导致棋钟提前显示为 0。
 */
export function elapsedMillisecondsFromServerSync(
  syncUtc,
  serverNowUtc,
  elapsedSinceSnapshotMs,
) {
  const syncMs = syncUtc ? Date.parse(syncUtc) : Number.NaN;
  const serverNowMs = serverNowUtc ? Date.parse(serverNowUtc) : Number.NaN;
  const elapsedBeforeSnapshot = Number.isFinite(syncMs) && Number.isFinite(serverNowMs)
    ? Math.max(0, serverNowMs - syncMs)
    : 0;
  return elapsedBeforeSnapshot + Math.max(0, elapsedSinceSnapshotMs);
}
