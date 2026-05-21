import type { Effect, EffectContext, EffectTime } from "./EffectBase";
import type { GameState } from "@/types/game";

const effectRegistry = new Map<string, Effect>();

export function registerEffect(effect: Effect): void {
  effectRegistry.set(effect.id, effect);
}

export function resolveEffect(
  effectId: string,
  state: GameState,
  ctx: EffectContext,
  timing: EffectTime
): Partial<GameState> | null {
  const effect = effectRegistry.get(effectId);
  if (!effect) return null;
  if (!effect.timing.includes(timing)) return null;
  if (effect.condition && !effect.condition(state, ctx)) return null;
  return effect.execute(state, ctx);
}

export function hasEffect(effectId: string): boolean {
  return effectRegistry.has(effectId);
}
