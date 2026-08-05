import { create } from "zustand";
import type { CardData } from "@/types/card";
import { colorMatch, compareCards } from "@/lib/cardSearch";

export { colorMatch, compareCards } from "@/lib/cardSearch";

// ── 对战格式 ──────────────────────────────────────────────────────────────
//
// OP15-Only:
//   - 主卡组 50 张（官方综合规则）
//   - 领航必须是 OP15 卡集
//   - 主卡组只能放 OP15 卡集
//   - 同名卡 ≤ 4
//
// 服务端 DeckValidator 是权威；这里只做前端引导与提示。
//
// Legacy40:
//   - 历史 40 张本地卡组，仅用于浏览/旧卡组兼容，不可用于网络对战。

export type DeckFormat = "Unrestricted" | "OP15-Only" | "OP16-Only";

interface FormatRule {
  mainSize: number;
  leaderSetWhitelist: string[] | null; // null = 不限
  mainSetWhitelist: string[] | null;
  label: string;
}

export const FORMAT_RULES: Record<DeckFormat, FormatRule> = {
  "Unrestricted": {
    mainSize: 50,
    leaderSetWhitelist: null,
    mainSetWhitelist: null,
    label: "无限制（50 张，任选卡集）",
  },
  "OP15-Only": {
    mainSize: 50,
    leaderSetWhitelist: ["OP15"],
    mainSetWhitelist: ["OP15"],
    label: "OP15 限定（50 张）",
  },
  "OP16-Only": {
    mainSize: 50,
    leaderSetWhitelist: ["OP16"],
    mainSetWhitelist: ["OP16"],
    label: "OP16 限定（50 张）",
  },
};

function setCodeOf(card: CardData): string {
  return card.number.split("-")[0];
}

// 不受「同名卡 ≤ 4」限制的卡（卡面注明"规则上，可以将任意张数的此卡牌放入卡组"）。
// 与服务端 DeckValidator.UnlimitedCopyCards 保持一致；新增同类卡时两处同步登记。
export const UNLIMITED_COPY_CARDS = new Set<string>([
  "OP01-075", // 和平主义者
  "OP08-072", // 饼干士兵
  "OP16-042", // 因佩尔地狱的囚犯
]);

function sortEntries(entries: DeckEntry[]): DeckEntry[] {
  return [...entries].sort((a, b) => compareCards(a.card, b.card));
}

export interface DeckEntry {
  card: CardData;
  count: number;
}

const GRID_COLS_KEY = "deckEditor_gridCols";
const FORMAT_KEY    = "deckEditor_format";
const MIN_COLS = 4;
const MAX_COLS = 16;

function loadGridCols(): number {
  if (typeof window === "undefined") return 8;
  const v = parseInt(localStorage.getItem(GRID_COLS_KEY) ?? "", 10);
  return isNaN(v) ? 8 : Math.min(MAX_COLS, Math.max(MIN_COLS, v));
}

export interface DeckNotice {
  message: string;
  type: "error" | "info";
}

interface DeckStore {
  format: DeckFormat;
  leader: CardData | null;
  entries: DeckEntry[];
  notice: DeckNotice | null;
  searchQuery: string;
  filterColors: string[];
  filterType: string;
  filterProperty: string;
  filterRarity: string;
  filterCost: number | null;   // 费用筛选，null = 全部
  filterSets: string[];        // 弹数（卡集）筛选，空数组 = 显示全部
  filterShowSub1: boolean;     // 是否显示角标=1 的卡（默认 false 隐藏）
  gridColumns: number;

  setFormat: (f: DeckFormat) => void;
  setLeader: (card: CardData | null) => void;
  addCard: (card: CardData) => void;
  removeCard: (number: string) => void;
  clearNotice: () => void;
  setSearchQuery: (q: string) => void;
  toggleFilterColor: (color: string) => void;
  clearFilterColors: () => void;
  setFilterType: (type: string) => void;
  setFilterProperty: (p: string) => void;
  setFilterRarity: (r: string) => void;
  setFilterCost: (c: number | null) => void;
  toggleFilterSet: (set: string) => void;
  clearFilterSets: () => void;
  setFilterShowSub1: (v: boolean) => void;
  setGridColumns: (n: number) => void;
  clearDeck: () => void;

  totalCards: () => number;
  isValid: () => boolean;
  validate: () => { ok: boolean; reason?: string };
  getCount: (number: string) => number;
  getMainSize: () => number;
}

function isCardAllowedInFormat(card: CardData, format: DeckFormat, isLeader: boolean): { ok: boolean; reason?: string } {
  const rule = FORMAT_RULES[format];
  const set = setCodeOf(card);
  const whitelist = isLeader ? rule.leaderSetWhitelist : rule.mainSetWhitelist;
  if (whitelist && !whitelist.includes(set)) {
    return { ok: false, reason: `${rule.label}：${isLeader ? "领航" : "卡牌"}必须来自 ${whitelist.join("/")} 卡集（当前 ${set}）` };
  }
  return { ok: true };
}

export const useDeckStore = create<DeckStore>((set, get) => ({
  format: "Unrestricted",   // 已移除格式选择 UI，恒为无限制（不再读取 localStorage 旧值）
  leader: null,
  entries: [],
  notice: null,
  searchQuery: "",
  filterColors: [],
  filterType: "",
  filterProperty: "",
  filterRarity: "",
  filterCost: null,
  filterSets: [],
  filterShowSub1: false,
  gridColumns: 8,

  setFormat: (f) => {
    if (typeof window !== "undefined") localStorage.setItem(FORMAT_KEY, f);
    set((s) => {
      // 切换格式后，将不符合新格式的卡牌移除
      const rule = FORMAT_RULES[f];
      let removed = 0;
      const entries = s.entries.filter((e) => {
        const allowed = isCardAllowedInFormat(e.card, f, false);
        if (!allowed.ok) removed += e.count;
        return allowed.ok;
      });
      let leader = s.leader;
      let leaderRemoved = false;
      if (leader && !isCardAllowedInFormat(leader, f, true).ok) {
        leader = null;
        leaderRemoved = true;
      }
      // 颜色不符的也移除
      if (leader) {
        let extra = 0;
        const finalEntries = entries.filter((e) => {
          const ok = colorMatch(leader!.color, e.card.color);
          if (!ok) extra += e.count;
          return ok;
        });
        removed += extra;
        return {
          format: f,
          leader,
          entries: finalEntries,
          notice: removed > 0 || leaderRemoved
            ? { message: `切换格式：${leaderRemoved ? "领航已清空，" : ""}移除 ${removed} 张不兼容卡牌`, type: "info" as const }
            : null,
        };
      }
      return {
        format: f,
        leader,
        entries,
        notice: removed > 0 || leaderRemoved
          ? { message: `切换格式：${leaderRemoved ? "领航已清空，" : ""}移除 ${removed} 张不兼容卡牌`, type: "info" as const }
          : null,
      };
    });
  },

  setLeader: (card) => {
    if (!card) {
      // 退出/清空领航：颜色筛选复位为「全部」
      set({ leader: null, filterColors: [], notice: null });
      return;
    }
    const fmt = get().format;
    const allowed = isCardAllowedInFormat(card, fmt, true);
    if (!allowed.ok) {
      set({ notice: { message: allowed.reason!, type: "error" as const } });
      return;
    }
    set((s) => {
      const removed = s.entries.reduce(
        (sum, e) => (!colorMatch(card.color, e.card.color) ? sum + e.count : sum),
        0
      );
      const entries = s.entries.filter((e) => colorMatch(card.color, e.card.color));
      // 颜色筛选自动收缩为领航颜色：单色直接选中该色，双色置「全部」（两色都显示，可再按需多选）
      const leaderColors = card.color.split("/");
      const filterColors = leaderColors.length === 1 ? leaderColors : [];
      return {
        leader: card,
        entries,
        filterColors,
        notice: removed > 0
          ? { message: `已自动移除 ${removed} 张颜色不符的卡牌`, type: "info" as const }
          : null,
      };
    });
  },

  addCard: (card) =>
    set((s) => {
      // 未选领航卡时禁止加卡：否则下方颜色校验因 `s.leader &&` 短路被跳过，
      // 任意颜色的卡都会被塞进卡组（bug #184）。
      if (!s.leader) {
        return { notice: { message: "请先选择领航卡，再添加卡牌", type: "error" as const } };
      }
      const allowed = isCardAllowedInFormat(card, s.format, false);
      if (!allowed.ok) {
        return { notice: { message: allowed.reason!, type: "error" as const } };
      }
      if (s.leader && !colorMatch(s.leader.color, card.color)) {
        return { notice: { message: "无法加入卡组：颜色与领航卡不符", type: "error" as const } };
      }
      const existing = s.entries.find((e) => e.card.number === card.number);
      if (existing) {
        if (existing.count >= 4 && !UNLIMITED_COPY_CARDS.has(card.number)) return s;
        return {
          entries: s.entries.map((e) =>
            e.card.number === card.number ? { ...e, count: e.count + 1 } : e
          ),
          notice: null,
        };
      }
      return { entries: sortEntries([...s.entries, { card, count: 1 }]), notice: null };
    }),

  removeCard: (number) =>
    set((s) => {
      const existing = s.entries.find((e) => e.card.number === number);
      if (!existing) return s;
      if (existing.count <= 1)
        return { entries: s.entries.filter((e) => e.card.number !== number) };
      return {
        entries: s.entries.map((e) =>
          e.card.number === number ? { ...e, count: e.count - 1 } : e
        ),
      };
    }),

  clearNotice: () => set({ notice: null }),

  setSearchQuery: (q) => set({ searchQuery: q }),
  toggleFilterColor: (color) => set((s) => ({
    filterColors: s.filterColors.includes(color)
      ? s.filterColors.filter((selectedColor) => selectedColor !== color)
      : [...s.filterColors, color],
  })),
  clearFilterColors: () => set({ filterColors: [] }),
  setFilterType: (type) => set({ filterType: type }),
  setFilterProperty: (p) => set({ filterProperty: p }),
  setFilterRarity: (r) => set({ filterRarity: r }),
  setFilterCost: (c) => set({ filterCost: c }),
  toggleFilterSet: (set_) =>
    set((s) => ({
      filterSets: s.filterSets.includes(set_)
        ? s.filterSets.filter((x) => x !== set_)
        : [...s.filterSets, set_],
    })),
  clearFilterSets: () => set({ filterSets: [] }),
  setFilterShowSub1: (v) => set({ filterShowSub1: v }),
  setGridColumns: (n) => {
    const clamped = Math.min(MAX_COLS, Math.max(MIN_COLS, n));
    if (typeof window !== "undefined")
      localStorage.setItem(GRID_COLS_KEY, String(clamped));
    set({ gridColumns: clamped });
  },
  clearDeck: () => set({ leader: null, entries: [], filterColors: [] }),

  totalCards: () => get().entries.reduce((sum, e) => sum + e.count, 0),

  isValid: () => get().validate().ok,

  validate: () => {
    const s = get();
    const rule = FORMAT_RULES[s.format];
    if (!s.leader) return { ok: false, reason: "请先选择领航卡" };
    if (s.totalCards() !== rule.mainSize)
      return { ok: false, reason: `主卡组应为 ${rule.mainSize} 张（当前 ${s.totalCards()}）` };
    // 颜色一致性 + 卡集白名单已在 add 时校验，这里二次复检
    for (const e of s.entries) {
      if (rule.mainSetWhitelist && !rule.mainSetWhitelist.includes(setCodeOf(e.card))) {
        return { ok: false, reason: `卡牌 ${e.card.number} 不属于 ${rule.label}` };
      }
      if (!colorMatch(s.leader.color, e.card.color)) {
        return { ok: false, reason: `卡牌 ${e.card.number} 颜色与领航不符` };
      }
    }
    return { ok: true };
  },

  getCount: (number) =>
    get().entries.find((e) => e.card.number === number)?.count ?? 0,

  getMainSize: () => FORMAT_RULES[get().format].mainSize,
}));
