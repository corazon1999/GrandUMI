import type { CardData } from "@/types/card";
import { CARD_SET_PATHS } from "./cardSets";
import { fetchCardData } from "./fetchCardData";

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

function parseNumericField(value: string | number | undefined): number {
  const parsed = Number(String(value ?? "").normalize("NFKC"));
  return Number.isFinite(parsed) ? parsed : 0;
}

// 图片 manifest：cardNumber → 所有版本 URL（正画在 [0]）
let imageManifest: Record<string, string[]> = {};
let pendingCardNumbers = new Set<string>();
let manifestLoaded = false;
let manifestPromise: Promise<void> | null = null;

async function ensureManifest(): Promise<void> {
  if (manifestLoaded) return;
  // 记忆化 Promise：并发加载多个卡集时，manifest 只请求一次
  if (!manifestPromise) {
    manifestPromise = (async () => {
      const [images, availability] = await Promise.allSettled([
        fetchCardData<Record<string, string[]>>("/data/imageManifest.json"),
        fetchCardData<{ cards?: Array<{ number?: string; state?: string }> }>("/data/_playability.v1.json"),
      ]);
      if (images.status === "fulfilled") imageManifest = images.value;
      if (availability.status === "fulfilled") {
          const playability = availability.value;
          pendingCardNumbers = new Set(
            (playability.cards ?? [])
              .filter((entry) => entry.state === "pending" && typeof entry.number === "string")
              .map((entry) => entry.number!),
          );
      }
      manifestLoaded = images.status === "fulfilled" && availability.status === "fulfilled";
      if (!manifestLoaded) console.warn("部分卡牌资料暂不可用，已保留成功加载的资料并允许重试。");
    })();
  }
  const pending = manifestPromise;
  await pending;
  if (!manifestLoaded && manifestPromise === pending) manifestPromise = null;
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
    power: parseNumericField(raw.power),
    cost: parseNumericField(raw.cost),
    counter: (() => {
      const v = String(raw.counter ?? "").normalize("NFKC");
      if (!v) return 0;
      const match = v.match(/\+?(\d+)/);
      return match ? parseInt(match[1], 10) : 0;
    })(),
    // 与服务端卡表解析保持一致，不能把逗号连接的多个特征当成一个特征。
    keyWords: raw.keyWords ? raw.keyWords.split(/[\/,，]/).map(keyword => keyword.trim()).filter(Boolean) : [],
    effectTags: raw.effectTags ?? [],
    abilities: raw.abilities ?? [],
    effectEvent: raw.effectEvent ?? "",
    sprite: sprites[0],
    image: raw.image,
    sprites,
    rarity: raw.rarity ?? "",
    subscript: parseNumericField(raw.subscript),
    trigger: raw.trigger ?? "",
    playability: pendingCardNumbers.has(raw.number) ? "pending" : "playable",
  };
}

const cardSetPromises = new Map<string, Promise<CardData[]>>();

export function loadCardSet(setName: string): Promise<CardData[]> {
  const existing = cardSetPromises.get(setName);
  if (existing) return existing;
  const path = CARD_SET_PATHS[setName];
  if (!path) return Promise.reject(new Error(`未知卡集：${setName}`));
  const pending = (async () => {
    // 卡集本体与附属资料同时下载，避免一个慢清单阻止全部卡集请求。
    const [raw] = await Promise.all([fetchCardData<RawCardData[]>(path), ensureManifest()]);
    const cards = raw.map(parseCard);
    cards.forEach((card) => cardCache.set(card.number, card));
    return cards;
  })();
  cardSetPromises.set(setName, pending);
  pending.catch(() => { if (cardSetPromises.get(setName) === pending) cardSetPromises.delete(setName); });
  return pending;
}

/** 只预加载当前卡组涉及的卡集，供匹配等待和准备界面复用。 */
export async function preloadDeckCards(deck: string): Promise<void> {
  const sets = new Set([...deck.matchAll(/\b([A-Z][A-Z0-9]*)-\d+/g)].map(match => match[1]));
  await Promise.all([...sets].filter(set => set in CARD_SET_PATHS).map(loadCardSet));
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
    const bundle: {
      manifest?: Record<string, string[]>;
      playability?: { cards?: Array<{ number?: string; state?: string }> };
      cards: RawCardData[];
    } = await fetchCardData("/data/allCards.json");

    // 单包自带 manifest，直接填充，避免再发一次 imageManifest.json 请求
    if (bundle.manifest) {
      imageManifest = bundle.manifest;
      manifestLoaded = true;
    }
    if (bundle.playability?.cards) {
      pendingCardNumbers = new Set(
        bundle.playability.cards
          .filter((entry) => entry.state === "pending" && typeof entry.number === "string")
          .map((entry) => entry.number!),
      );
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

function correctKnownWrongSprite(number: string, sprite: string): string {
  // 旧卡组曾把两张山治卡图保存为卡鲁；保留卡组选择时定向回退已核实的卡鲁图。
  if (number === "EB02-001" && ["/cards/eb02/EB02-001.png", "/cards/eb02/EB02-001_01.png"]
    .includes(sprite.split("?")[0])) return "/cards/eb02/EB02-001_alt2.png";
  return sprite;
}

/**
 * 按对局中某一方的卡组选择返回卡面，避免全局卡牌缓存让双方或观战视角串用异画。
 * 旧快照没有 spriteMap 时固定回退该卡正画，不继承组卡器曾写入缓存的临时选择。
 */
export function getGameCard(
  number: string,
  spriteMap?: Record<string, string>,
): CardData | undefined {
  const card = getCard(number);
  if (!card) return undefined;
  const sprite = correctKnownWrongSprite(number,
    spriteMap?.[number] ?? card.sprites?.[0] ?? card.image ?? card.sprite);
  return sprite === card.sprite ? card : { ...card, sprite };
}

/** 覆盖缓存中指定卡号的 sprite（用于还原异画选择） */
export function applyCachedSprite(number: string, sprite: string): void {
  const card = cardCache.get(number);
  if (card) card.sprite = correctKnownWrongSprite(number, sprite);
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
