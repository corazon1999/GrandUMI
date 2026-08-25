import type { MatchMeta } from "./matchHistoryDB";

interface OpeningSnapshot {
  firstPlayerChosen?: boolean;
  diceWinnerIsMe?: boolean;
  isFirstPlayer?: boolean;
}

export type MatchOpeningMeta = Pick<MatchMeta, "diceWinnerIsMe" | "isFirstPlayer">;

/**
 * 仅在先后手已经确定后保存开局结果。
 * 这样旧快照或开局流程未完成时的布尔默认值不会被误记为“骰子负 / 后手”。
 */
export function extractMatchOpeningMeta(snapshot: OpeningSnapshot): MatchOpeningMeta {
  if (snapshot.firstPlayerChosen !== true) return {};

  const opening: MatchOpeningMeta = {};
  if (typeof snapshot.diceWinnerIsMe === "boolean") {
    opening.diceWinnerIsMe = snapshot.diceWinnerIsMe;
  }
  if (typeof snapshot.isFirstPlayer === "boolean") {
    opening.isFirstPlayer = snapshot.isFirstPlayer;
  }
  return opening;
}

/** 旧记录缺少字段时返回空数组，由列表隐藏未知状态，避免误导。 */
export function getMatchOpeningLabels(meta: MatchOpeningMeta): string[] {
  const labels: string[] = [];
  if (typeof meta.diceWinnerIsMe === "boolean") {
    labels.push(meta.diceWinnerIsMe ? "骰子：胜" : "骰子：负");
  }
  if (typeof meta.isFirstPlayer === "boolean") {
    labels.push(meta.isFirstPlayer ? "先手" : "后手");
  }
  return labels;
}
