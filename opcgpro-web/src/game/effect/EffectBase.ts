import type { GameState } from "@/types/game";

export type EffectTime =
  | "Main"
  | "Counter"
  | "Trigger"
  | "Attack"
  | "BeAttack"
  | "Block"
  | "Appear"
  | "OnKO"
  | "TurnStart"
  | "TurnEnd"
  | "Damage"
  | "BeDamage";

export interface EffectContext {
  cardIndex?: number;
  targetIndex?: number;
  side: "my" | "opponent";
  extraData?: unknown;
}

export interface Effect {
  id: string;
  timing: EffectTime[];
  condition?: (state: GameState, ctx: EffectContext) => boolean;
  execute: (state: GameState, ctx: EffectContext) => Partial<GameState>;
}
