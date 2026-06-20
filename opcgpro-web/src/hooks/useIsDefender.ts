"use client";

import { useGameStore } from "@/store/gameStore";

/**
 * 判断「我是否为当前这场战斗的防守方」。
 *
 * 用于反击/阻挡的操作权判断：防守方 = 攻击者卡属于对手。
 * 不依赖 currentTurn —— 正常对局里防守方恒为非当前回合方，与 !currentTurn 等价；
 * 但 GM「对手领袖攻击」会在我方回合制造「对手攻击我」的战斗（currentTurn 仍为 true），
 * 此时必须靠攻击者归属判断，才能让防守方（我）拿到反击/阻挡操作权。
 */
export function useIsDefender(): boolean {
  const battle = useGameStore((s) => s.battle);
  const opp = useGameStore((s) => s.opponent);
  if (!battle || !opp) return false;
  return (
    battle.attackerCardId === opp.leaderId ||
    opp.fieldCards.some((c) => c.id === battle.attackerCardId)
  );
}
