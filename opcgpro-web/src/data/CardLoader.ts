import type { CardData } from "@/types/card";
import { CARD_SET_PATHS } from "./cardSets";

interface RawCardData {
  number: string;
  name: string;
  color: string;
  type: string;
  property: string;
  power: string;
  cost: string;
  keyWords: string;
  counter: string;
  effectText: string;
  effectEvent: string;
  rarity: string;
  subscript: number | string;
  trigger: string;
}

const TYPE_MAP: Record<string, CardData["type"]> = {
  "领航": "Leader",
  "角色": "Character",
  "舞台": "Stage",
  "事件": "Event",
};

const cardCache = new Map<string, CardData>();

// 图片 manifest：cardNumber → 所有版本 URL（正画在 [0]）
let imageManifest: Record<string, string[]> = {};
let manifestLoaded = false;

async function ensureManifest(): Promise<void> {
  if (manifestLoaded) return;
  try {
    const res = await fetch("/data/imageManifest.json");
    if (res.ok) imageManifest = await res.json();
  } catch {
    // manifest 加载失败时降级：每张卡只有默认正画
  }
  manifestLoaded = true;
}

function parseCard(raw: RawCardData): CardData {
  const setPrefix = raw.number.split("-")[0];
  const defaultSprite = `/cards/${setPrefix.toLowerCase()}/${raw.number}.png`;
  const sprites = imageManifest[raw.number] ?? [defaultSprite];
  return {
    number: raw.number,
    name: raw.name,
    color: raw.color,
    type: (TYPE_MAP[raw.type] ?? raw.type) as CardData["type"],
    property: raw.property as CardData["property"],
    power: Number(raw.power) || 0,
    cost: Number(raw.cost) || 0,
    counter: Number(raw.counter) || 0,
    keyWords: raw.keyWords ? raw.keyWords.split("/").filter(Boolean) : [],
    effectText: raw.effectText ?? "",
    effectEvent: raw.effectEvent ?? "",
    sprite: sprites[0],
    sprites,
    rarity: raw.rarity ?? "",
    subscript: Number(raw.subscript) || 0,
    trigger: raw.trigger ?? "",
  };
}

export async function loadCardSet(setName: string): Promise<CardData[]> {
  const path = CARD_SET_PATHS[setName];
  if (!path) throw new Error(`未知卡集: ${setName}`);

  await ensureManifest();

  const res = await fetch(path);
  if (!res.ok) throw new Error(`加载卡集失败: ${setName} (${res.status})`);

  const raw: RawCardData[] = await res.json();
  const cards = raw.map(parseCard);
  cards.forEach((c) => cardCache.set(c.number, c));
  return cards;
}

export function getCard(number: string): CardData | undefined {
  return cardCache.get(number);
}

export function getAllCachedCards(): CardData[] {
  return Array.from(cardCache.values());
}

export async function preloadAllCardSets(): Promise<void> {
  const results = await Promise.allSettled(
    Object.keys(CARD_SET_PATHS).map(loadCardSet)
  );
  const failed = results.filter((r) => r.status === "rejected");
  if (failed.length > 0) {
    console.warn(`${failed.length} 个卡集加载失败`);
  }
}
