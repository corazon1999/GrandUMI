/**
 * battleStore.ts — 战斗交互的纯本地状态
 *
 * 不参与结算。所有最终动作通过 GameRequest 发到服务端，由服务端 BattleEngine 推进战斗。
 */

import { createStore } from "zustand/vanilla";
import type { BattlePhase } from "@/types/game";
import { GameRequest } from "@/net/GameRequest";

interface BattleStore {
  // 暂态：选了攻击者后等待选目标
  isSelectingTarget: boolean;
  attackerId: string | null;

  startAttack:         (attackerId: string) => void;
  confirmAttackTarget: (target: { isLeader: true } | { isLeader: false; cardId: string }) => void;
  cancelAttack:        () => void;
  endTurn:             () => void;

  // 兼容旧 API
  phase: BattlePhase;
  setPhase: (p: BattlePhase) => void;
  setAttacker: (id: string | null) => void;
  setDefender: (id: string | null) => void;
  defenderId: string | null;

  reset: () => void;
}

export const useBattleStore = createStore<BattleStore>()((set, get) => ({
  isSelectingTarget: false,
  attackerId: null,
  phase: "Main",
  defenderId: null,

  startAttack: (attackerId) => set({ isSelectingTarget: true, attackerId }),

  confirmAttackTarget: (target) => {
    const { attackerId } = get();
    if (!attackerId) return;
    GameRequest.attack(attackerId, target);
    set({ isSelectingTarget: false, attackerId: null });
  },

  cancelAttack: () => set({ isSelectingTarget: false, attackerId: null }),

  endTurn: () => GameRequest.endTurn(),

  setPhase: (p) => set({ phase: p }),
  setAttacker: (id) => set({ attackerId: id }),
  setDefender: (id) => set({ defenderId: id }),

  reset: () => set({ isSelectingTarget: false, attackerId: null, defenderId: null, phase: "Main" }),
}));
