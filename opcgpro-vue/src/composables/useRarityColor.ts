/**
 * 稀有度颜色映射（HS-现代 配色）
 * - L (Leader)  : 金
 * - SEC         : 渐变紫
 * - SR          : 紫红
 * - R           : 蓝
 * - UC          : 银
 * - C           : 灰
 * - P (Promo)   : 青
 */
export type Rarity = "L" | "SEC" | "SR" | "R" | "UC" | "C" | "P" | string;

export const RARITY_RING: Record<string, { border: string; glow: string; label: string }> = {
  L:   { border: "#c8a04a", glow: "rgba(200,160,74,0.45)",  label: "Leader 金" },
  SEC: { border: "#a855f7", glow: "rgba(168,85,247,0.45)",  label: "Secret 紫" },
  SR:  { border: "#ec4899", glow: "rgba(236,72,153,0.45)",  label: "Super Rare 粉" },
  R:   { border: "#3b82f6", glow: "rgba(59,130,246,0.45)",  label: "Rare 蓝" },
  UC:  { border: "#9ca3af", glow: "rgba(156,163,175,0.45)", label: "Uncommon 银" },
  C:   { border: "#6b7280", glow: "rgba(107,114,128,0.45)", label: "Common 灰" },
  P:   { border: "#14b8a6", glow: "rgba(20,184,166,0.45)",  label: "Promo 青" },
};

export function getRarityRing(rarity: string) {
  return RARITY_RING[rarity] ?? RARITY_RING.C;
}
