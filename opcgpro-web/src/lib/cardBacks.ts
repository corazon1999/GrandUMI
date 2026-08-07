export const CARD_BACK_OPTIONS = [
  {
    id: "classic",
    name: "经典",
    description: "GrandUMI 深海蓝经典款",
  },
  {
    id: "straw-hat",
    name: "草帽",
    description: "红金配色的冒险主题",
  },
  {
    id: "marine",
    name: "海军",
    description: "蓝白配色的正义主题",
  },
  {
    id: "emperor",
    name: "四皇",
    description: "紫黑配色的霸者主题",
  },
] as const;

export type CardBackId = (typeof CARD_BACK_OPTIONS)[number]["id"];
export const DEFAULT_CARD_BACK_ID: CardBackId = "classic";

const CARD_BACK_IDS = new Set<string>(CARD_BACK_OPTIONS.map((option) => option.id));

export function normalizeCardBackId(value: string | null | undefined): CardBackId {
  return CARD_BACK_IDS.has(value ?? "") ? (value as CardBackId) : DEFAULT_CARD_BACK_ID;
}

export function cardBackName(value: string | null | undefined): string {
  const id = normalizeCardBackId(value);
  return CARD_BACK_OPTIONS.find((option) => option.id === id)?.name ?? "经典";
}
