import type { CardData } from "@/types/card";
import { CARD_SET_PATHS } from "./cardSets";
import { DATA_VERSION } from "./dataVersion";

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
  effectTags: string[];
  abilities: string[];
  effectText: string;
  effectEvent: string;
  rarity: string;
  subscript: number | string;
  trigger: string;
  image?: string;
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
  const defaultSprite = `/cards/${setPrefix.toLowerCase()}/${raw.number}.webp`;
  const sprites = imageManifest[raw.number] ?? [defaultSprite];
  return {
    number: raw.number,
    name: raw.name,
    color: raw.color,
    type: (TYPE_MAP[raw.type] ?? raw.type) as CardData["type"],
    property: raw.property as CardData["property"],
    power: Number(raw.power) || 0,
    cost: Number(raw.cost) || 0,
    counter: (() => {
      const v = raw.counter;
      if (!v) return 0;
      const match = String(v).match(/\+?(\d+)/);
      return match ? parseInt(match[1], 10) : 0;
    })(),
    keyWords: raw.keyWords ? raw.keyWords.split("/").filter(Boolean) : [],
    effectTags: raw.effectTags ?? [],
    abilities: raw.abilities ?? [],
    effectText: raw.effectText ?? "",
    effectEvent: raw.effectEvent ?? "",
    sprite: sprites[0],
    image: raw.image,
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

// ── 全量加载（记忆化） ──────────────────────────────────────────────────

let allCardsPromise: Promise<void> | null = null;

export function loadAllCards(): Promise<void> {
  if (allCardsPromise) return allCardsPromise;
  allCardsPromise = (async () => {
    const res = await fetch(`/data/allCards.json?v=${DATA_VERSION}`);
    if (!res.ok) throw new Error(`加载卡牌总包失败 (${res.status})`);

    const bundle: { manifest?: Record<string, string[]>; cards: RawCardData[] } = await res.json();

    if (bundle.manifest) {
      imageManifest = bundle.manifest;
      manifestLoaded = true;
    }

    bundle.cards.map(parseCard).forEach((c) => cardCache.set(c.number, c));
  })();
  allCardsPromise.catch(() => { allCardsPromise = null; });
  return allCardsPromise;
}

// ── 异画支持 ───────────────────────────────────────────────────────────

export function applyCachedSprite(number: string, sprite: string): void {
  const card = cardCache.get(number);
  if (card) card.sprite = sprite;
}

export function applySpriteMap(map: Record<string, string>): void {
  for (const [num, sprite] of Object.entries(map)) {
    applyCachedSprite(num, sprite);
  }
}
