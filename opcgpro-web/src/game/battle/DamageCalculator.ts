import type { FieldCardState } from "@/types/game";

export function calculateDamage(
  attacker: FieldCardState,
  defender: FieldCardState | null
): number {
  const attackPower = attacker.card.power + attacker.powerBuff;
  if (!defender) return attackPower;
  const defendPower = defender.card.power + defender.powerBuff;
  return Math.max(0, attackPower - defendPower);
}

export function canAttack(card: FieldCardState): boolean {
  return !card.isTapped;
}

export function canBlock(card: FieldCardState): boolean {
  return !card.isTapped;
}
