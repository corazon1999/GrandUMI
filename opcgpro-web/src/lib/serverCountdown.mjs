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
