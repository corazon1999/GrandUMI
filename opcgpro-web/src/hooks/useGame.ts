"use client";

import { useGameStore } from "@/store/gameStore";
import { useBattleStore } from "@/store/battleStore";
import { GameRequest } from "@/net/GameRequest";

export function useGame() {
  const game = useGameStore();
  const battle = useBattleStore();

  // 出牌
  const playCard = (handIndex: number) => {
    GameRequest.playCard(handIndex);
  };

  // 宣言攻击
  const declareAttack = (attackerIndex: number, targetIndex: number) => {
    GameRequest.attack(attackerIndex, targetIndex);
    battle.setAttacker(attackerIndex);
    battle.setDefender(targetIndex);
    battle.setPhase("Attack");
  };

  // 结束回合
  const endTurn = () => {
    GameRequest.endTurn();
  };

  // 投降
  const surrender = () => {
    GameRequest.surrender();
  };

  return {
    ...game,
    battle,
    endTurn,
    playCard,
    declareAttack,
    surrender,
  };
}
