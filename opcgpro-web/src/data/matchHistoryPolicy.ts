/** 判断终局原因是否表示对局因玩家断线而结束。 */
export function isDisconnectFinishReason(reason: string | null | undefined): boolean {
  if (!reason) return false;
  return reason.includes("断线") || reason.toLowerCase().includes("disconnect");
}

/**
 * 断线败局不进入玩家可见的本地战绩；断线获胜、平局和正常败局仍保留。
 * 该规则同时用于新记录写入和旧记录清理，避免两处行为漂移。
 */
export function shouldHideDisconnectLoss(match: {
  winnerIsMe: boolean;
  isDraw?: boolean;
  gameOverReason: string;
}): boolean {
  return !match.isDraw
    && !match.winnerIsMe
    && isDisconnectFinishReason(match.gameOverReason);
}
