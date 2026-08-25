import type { BattlePhase } from "@/types/game";

export type BattleTargetMarkerTone = "target" | "blocker" | "under-attack";

export interface BattleTargetMarker {
  text: "目标" | "阻挡" | "被攻击";
  ariaLabel: string;
  tone: BattleTargetMarkerTone;
}

/**
 * 仅给服务端权威 battle 快照认定的当前目标生成标识。
 * 反击阶段的文案明确说明反击值会保护谁，避免与本地“已选中”状态混淆。
 */
export function getBattleTargetMarker({
  phase,
  isBattleTarget,
  isBlocker,
}: {
  phase: BattlePhase;
  isBattleTarget: boolean;
  isBlocker: boolean;
}): BattleTargetMarker | null {
  if (!isBattleTarget) return null;

  if (phase === "Counter") {
    return {
      text: "被攻击",
      ariaLabel: isBlocker
        ? "当前被攻击对象（阻挡角色），反击值将用于保护此角色"
        : "当前被攻击对象，反击值将用于保护此对象",
      tone: "under-attack",
    };
  }

  if (isBlocker) {
    return {
      text: "阻挡",
      ariaLabel: "当前阻挡角色",
      tone: "blocker",
    };
  }

  return {
    text: "目标",
    ariaLabel: "当前战斗目标",
    tone: "target",
  };
}
