/**
 * battleStore.ts — 战斗交互状态
 * 对应 C# BattleManager.cs 的 UI 交互部分
 *
 * 架构原则（重构方案 §5.7）：
 *   只管理"选谁攻击谁"的 UI 交互状态
 *   所有操作最终调用 GameRequest 发送服务器，不在本地结算
 */

import { create } from "zustand";
import type { BattlePhase } from "@/types/game";
import { GameRequest } from "@/net/GameRequest";
import { useGameStore } from "./gameStore";

interface BattleStore {
  // 战斗阶段（同步自服务器）
  phase: BattlePhase;
  // 攻击者/防御者索引
  attackerIndex: number | null;
  defenderIndex: number | null;
  // 是否正在选择攻击目标
  isSelectingTarget: boolean;
  // 是否正在处理效果
  isResolvingEffect: boolean;

  // ── 基本 setter ──────────────────────────────────────────────────────
  setPhase: (phase: BattlePhase) => void;
  setAttacker: (index: number | null) => void;
  setDefender: (index: number | null) => void;
  setResolvingEffect: (v: boolean) => void;

  // ── 攻击交互流程（调用 GameRequest 发送服务器）───────────────────────
  /** 选择己方攻击者，进入选目标模式 */
  startAttack: (attackerIndex: number) => void;
  /** 确认攻击目标（对方角色索引或领航卡 'leader'） */
  confirmAttackTarget: (target: number | "leader") => void;
  /** 取消攻击 */
  cancelAttack: () => void;

  // ── 快捷操作 ─────────────────────────────────────────────────────────
  /** 打出当前选中的手牌 */
  playSelectedCard: () => void;
  /** 结束当前回合 */
  endTurn: () => void;

  // 重置
  reset: () => void;
}

export const useBattleStore = create<BattleStore>((set, get) => ({
  phase: "Main",
  attackerIndex: null,
  defenderIndex: null,
  isSelectingTarget: false,
  isResolvingEffect: false,

  setPhase: (phase) => set({ phase }),
  setAttacker: (index) => set({ attackerIndex: index }),
  setDefender: (index) => set({ defenderIndex: index }),
  setResolvingEffect: (v) => set({ isResolvingEffect: v }),

  // ── 攻击交互流程 ─────────────────────────────────────────────────────

  startAttack: (attackerIndex) =>
    set({ isSelectingTarget: true, attackerIndex }),

  confirmAttackTarget: (target) => {
    const { attackerIndex } = get();
    if (attackerIndex === null) return;
    GameRequest.attack(attackerIndex, target);
    set({ isSelectingTarget: false, attackerIndex: null });
  },

  cancelAttack: () =>
    set({ isSelectingTarget: false, attackerIndex: null }),

  // ── 快捷操作 ─────────────────────────────────────────────────────────

  playSelectedCard: () => {
    const { selectedHandIndex } = useGameStore.getState();
    if (selectedHandIndex === null) return;
    GameRequest.playCard(selectedHandIndex);
  },

  endTurn: () => {
    GameRequest.endTurn();
  },

  reset: () =>
    set({
      phase: "Main",
      attackerIndex: null,
      defenderIndex: null,
      isSelectingTarget: false,
      isResolvingEffect: false,
    }),
}));
