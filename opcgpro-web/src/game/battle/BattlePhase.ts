export type { BattlePhase } from "@/types/game";

export const PHASE_LABELS: Record<string, string> = {
  Main: "主阶段",
  Attack: "攻击阶段",
  Block: "防御阶段",
  Counter: "反击阶段",
  Damage: "伤害计算",
  End: "回合结束",
};
