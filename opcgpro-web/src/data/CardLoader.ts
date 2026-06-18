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
let manifestPromise: Promise<void> | null = null;

async function ensureManifest(): Promise<void> {
  if (manifestLoaded) return;
  // 记忆化 Promise：并发加载多个卡集时，manifest 只请求一次
  if (!manifestPromise) {
    manifestPromise = (async () => {
      try {
        const res = await fetch("/data/imageManifest.json");
        if (res.ok) imageManifest = await res.json();
      } catch {
        // manifest 加载失败时降级：每张卡只有默认正画
      }
      manifestLoaded = true;
    })();
  }
  return manifestPromise;
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
    counter: (() => {
      const v = raw.counter;
      if (!v) return 0;
      const match = String(v).match(/\+?(\d+)/);
      return match ? parseInt(match[1], 10) : 0;
    })(),
    keyWords: raw.keyWords ? raw.keyWords.split("/").filter(Boolean) : [],
    effectTags: raw.effectTags ?? [],
    abilities: raw.abilities ?? [],
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

/**
 * 一次性加载全部卡牌（卡组编辑器快速通道）。
 * 请求合并后的单包 allCards.json，附带内容哈希做缓存击穿：
 * 内容不变时浏览器永久走磁盘缓存（零网络），改卡后哈希变自动重新下载一次。
 */
let allCardsPromise: Promise<void> | null = null;

export function loadAllCards(): Promise<void> {
  // 记忆化：全量包只下载一次（对战页与卡组编辑器共享缓存，避免重复请求）
  if (allCardsPromise) return allCardsPromise;
  allCardsPromise = (async () => {
    const res = await fetch(`/data/allCards.json?v=${DATA_VERSION}`);
    if (!res.ok) throw new Error(`加载卡牌总包失败 (${res.status})`);

    const bundle: { manifest?: Record<string, string[]>; cards: RawCardData[] } = await res.json();

    // 单包自带 manifest，直接填充，避免再发一次 imageManifest.json 请求
    if (bundle.manifest) {
      imageManifest = bundle.manifest;
      manifestLoaded = true;
    }

    bundle.cards.map(parseCard).forEach((c) => cardCache.set(c.number, c));
  })();
  // 失败时清空记忆化，允许后续重试
  allCardsPromise.catch(() => { allCardsPromise = null; });
  return allCardsPromise;
}

export function getCard(number: string): CardData | undefined {
  return cardCache.get(number);
}

/** 覆盖缓存中指定卡号的 sprite（用于还原异画选择） */
export function applyCachedSprite(number: string, sprite: string): void {
  const card = cardCache.get(number);
  if (card) card.sprite = sprite;
}

/** 批量覆盖缓存中的异画 */
export function applySpriteMap(map: Record<string, string>): void {
  for (const [num, sprite] of Object.entries(map)) {
    applyCachedSprite(num, sprite);
  }
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
