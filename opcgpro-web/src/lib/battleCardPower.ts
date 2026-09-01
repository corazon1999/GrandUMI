import type { BattleView } from "@/store/gameStore";

type BattleCardKind = "leader" | "character";

/**
 * 返回某张场上卡牌在当前战斗中独有的权威力量加成。
 *
 * player.leaderPower / fieldCard.powerCurrent 已含卡面基础值、咚和通常的力量修正；
 * BattleView 的两个 bonus 只在本次战斗中额外结算，不能写回玩家常驻力量。
 */
export function getBattleCardPowerBonus(
  battle: BattleView | null,
  cardId: string,
  kind: BattleCardKind,
): number {
  if (!battle) return 0;

  if (cardId === battle.attackerCardId) {
    return battle.attackerBonus ?? 0;
  }

  const isEffectiveDefender = battle.blockerCardId
    ? kind === "character" && cardId === battle.blockerCardId
    : battle.targetIsLeader
      ? kind === "leader"
      : kind === "character" && cardId === battle.targetCardId;

  return isEffectiveDefender ? (battle.defenderBonus ?? 0) : 0;
}
