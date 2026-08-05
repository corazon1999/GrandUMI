import { ALL_SET_NAMES } from "@/data/cardSets";
import type { CardData } from "@/types/card";

export const CARD_PROPERTIES = ["", "斩", "打", "射", "智", "特"];
export const CARD_TYPES = ["", "Character", "Stage", "Event"];
export const CARD_RARITIES = ["", "L", "SR", "R", "UC", "C", "SEC", "P"];
export const CARD_COSTS = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

export const CARD_SET_GROUPS: { label: string; sets: string[] }[] = [
  { label: "OP 主弹", sets: ALL_SET_NAMES.filter((set) => set.startsWith("OP")) },
  { label: "ST 起始", sets: ALL_SET_NAMES.filter((set) => set.startsWith("ST")) },
  {
    label: "EB/PRB",
    sets: ALL_SET_NAMES.filter((set) => set.startsWith("EB") || set.startsWith("PRB")),
  },
  {
    label: "P/其他",
    sets: ALL_SET_NAMES.filter(
      (set) =>
        !set.startsWith("OP") &&
        !set.startsWith("ST") &&
        !set.startsWith("EB") &&
        !set.startsWith("PRB"),
    ),
  },
];

export interface CardSearchFilters {
  searchQuery: string;
  filterColors: string[];
  filterType: string;
  filterProperty: string;
  filterRarity: string;
  filterCost: number | null;
  filterSets: string[];
  filterShowSub1: boolean;
}

interface CardSearchOptions {
  allowedSets?: string[] | null;
  leaderColor?: string | null;
  /** 图鉴的“全部类型”需要包含领航；组卡普通模式则排除领航。 */
  includeLeadersWhenAllTypes?: boolean;
  /** 默认使用组卡排序；图鉴可传入按卡号排列的专用比较器。 */
  sortComparator?: (a: CardData, b: CardData) => number;
}

export function cardSetOf(card: CardData): string {
  return card.number.split("-")[0];
}

export function colorMatch(leaderColor: string, cardColor: string): boolean {
  const leaderColors = new Set(leaderColor.split("/"));
  return cardColor.split("/").some((color) => leaderColors.has(color));
}

const TYPE_ORDER: Record<string, number> = {
  Character: 0,
  Event: 1,
  Stage: 2,
};

// 发售顺序：越靠后表示越晚发售，同费用下排在越前面。
const SET_RELEASE_ORDER: Record<string, number> = (() => {
  const order = [
    "OP01",    "ST01","ST02","ST03","ST04","ST05","ST06",
    "OP02",    "ST07",
    "ST08","ST09",
    "EB01",
    "OP03",    "ST10",
    "OP04",    "EB02",
    "ST11","ST12",
    "OP05",    "ST13",
    "ST14",
    "OP06",
    "EB03",
    "OP07",    "ST15","ST16","ST17",
    "OP08",    "ST18","ST19","ST20",
    "OP09",    "ST21",
    "PRB01",
    "OP10",    "ST22","ST23","ST24","ST25","ST26",
    "OP11",    "EB04",
    "OP12",    "ST27","ST28",
    "OP13",    "PRB02",
    "OP14",    "ST29","ST30",
    "OP15",
    "OP16",    "ST31","ST32","ST33","ST34","ST35","ST36",
    "P",
  ];
  const map: Record<string, number> = {};
  order.forEach((code, index) => {
    map[code] = index + 1;
  });
  return map;
})();

function compareSetNewness(a: string, b: string): number {
  const orderA = SET_RELEASE_ORDER[a] ?? 0;
  const orderB = SET_RELEASE_ORDER[b] ?? 0;
  if (orderA !== orderB) return orderB - orderA;
  return b.localeCompare(a);
}

/**
 * 统一排序：
 * 领航为角标↓ → 发售时间新→旧 → 卡号；
 * 其他卡牌为费用↑ → 角标↓ → 发售时间新→旧 → 角色/事件/场地 → 卡号。
 */
export function compareCards(a: CardData, b: CardData): number {
  if (a.type === "Leader" && b.type === "Leader") {
    if (a.subscript !== b.subscript) return b.subscript - a.subscript;
    const setComparison = compareSetNewness(cardSetOf(a), cardSetOf(b));
    if (setComparison !== 0) return setComparison;
    return a.number.localeCompare(b.number);
  }
  if (a.cost !== b.cost) return a.cost - b.cost;
  if (a.subscript !== b.subscript) return b.subscript - a.subscript;
  const setComparison = compareSetNewness(cardSetOf(a), cardSetOf(b));
  if (setComparison !== 0) return setComparison;
  const typeOrderA = TYPE_ORDER[a.type] ?? 99;
  const typeOrderB = TYPE_ORDER[b.type] ?? 99;
  if (typeOrderA !== typeOrderB) return typeOrderA - typeOrderB;
  return a.number.localeCompare(b.number);
}

const CATALOG_SET_ORDER = new Map(
  ALL_SET_NAMES.map((setName, index) => [setName, index]),
);

/** 图鉴排序：按卡集分组，每个卡集内按卡号数字从低到高排列。 */
export function compareCatalogCards(a: CardData, b: CardData): number {
  const setA = cardSetOf(a);
  const setB = cardSetOf(b);
  const orderA = CATALOG_SET_ORDER.get(setA) ?? Number.MAX_SAFE_INTEGER;
  const orderB = CATALOG_SET_ORDER.get(setB) ?? Number.MAX_SAFE_INTEGER;

  if (orderA !== orderB) return orderA - orderB;

  const setComparison = setA.localeCompare(setB, undefined, { numeric: true });
  if (setComparison !== 0) return setComparison;

  return a.number.localeCompare(b.number, undefined, { numeric: true });
}

/** 图鉴与组卡界面共用的筛选和排序入口。 */
export function filterAndSortCards(
  cards: CardData[],
  filters: CardSearchFilters,
  options: CardSearchOptions = {},
): CardData[] {
  const {
    searchQuery,
    filterColors,
    filterType,
    filterProperty,
    filterRarity,
    filterCost,
    filterSets,
    filterShowSub1,
  } = filters;
  const isLeaderMode = filterType === "Leader";
  const normalizedQuery = searchQuery.trim().toLocaleLowerCase();
  const selectedColors = new Set(filterColors);

  return cards
    .filter((card) => {
      const setCode = cardSetOf(card);
      if (options.allowedSets && !options.allowedSets.includes(setCode)) return false;
      if (filterSets.length > 0 && !filterSets.includes(setCode)) return false;
      if (!filterShowSub1 && card.subscript === 1) return false;

      if (isLeaderMode && card.type !== "Leader") return false;
      if (!isLeaderMode && !options.includeLeadersWhenAllTypes && card.type === "Leader") {
        return false;
      }
      if (!isLeaderMode && options.leaderColor && !colorMatch(options.leaderColor, card.color)) {
        return false;
      }

      if (
        selectedColors.size > 0 &&
        !card.color.split("/").some((color) => selectedColors.has(color))
      ) {
        return false;
      }
      if (filterProperty && card.property !== filterProperty) return false;
      if (filterRarity && card.rarity !== filterRarity) return false;
      if (filterCost !== null && card.cost !== filterCost) return false;
      if (!isLeaderMode && filterType && card.type !== filterType) return false;

      if (
        normalizedQuery &&
        !card.name.toLocaleLowerCase().includes(normalizedQuery) &&
        !card.number.toLocaleLowerCase().includes(normalizedQuery) &&
        !card.keyWords.some((keyword) =>
          keyword.toLocaleLowerCase().includes(normalizedQuery),
        )
      ) {
        return false;
      }
      return true;
    })
    .sort(options.sortComparator ?? compareCards);
}
