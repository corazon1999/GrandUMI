"use client";

import { useGameStore } from "@/store/gameStore";
import { useBattleStore } from "@/store/battleStore";
import { GameRequest } from "@/net/GameRequest";

export function useGame() {
  const game = useGameStore();
  const battle = useBattleStore();

  return {
    ...game,
    battle,
    playCard:       (handIndex: number) => GameRequest.playCard(handIndex),
    declareAttack:  (attackerId: string, target: { isLeader: true } | { isLeader: false; cardId: string }) =>
                       GameRequest.attack(attackerId, target),
    endTurn:        () => GameRequest.endTurn(),
    surrender:      () => GameRequest.surrender(),
  };
}
