import { create } from "zustand";
import type { CardData } from "@/types/card";

function colorMatch(leaderColor: string, cardColor: string): boolean {
  const leaderColors = new Set(leaderColor.split("/"));
  return cardColor.split("/").every((c) => leaderColors.has(c));
}

const TYPE_ORDER: Record<string, number> = {
  Character: 0,
  Event: 1,
  Stage: 2,
};

function sortEntries(entries: DeckEntry[]): DeckEntry[] {
  return [...entries].sort((a, b) => {
    const orderA = TYPE_ORDER[a.card.type] ?? 99;
    const orderB = TYPE_ORDER[b.card.type] ?? 99;
    if (orderA !== orderB) return orderA - orderB;
    return a.card.cost - b.card.cost;
  });
}

export interface DeckEntry {
  card: CardData;
  count: number;
}

const GRID_COLS_KEY = "deckEditor_gridCols";
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
  leader: CardData | null;
  entries: DeckEntry[];
  notice: DeckNotice | null;
  searchQuery: string;
  filterColor: string;
  filterType: string;
  filterProperty: string;
  filterRarity: string;
  gridColumns: number;

  setLeader: (card: CardData | null) => void;
  addCard: (card: CardData) => void;
  removeCard: (number: string) => void;
  clearNotice: () => void;
  setSearchQuery: (q: string) => void;
  setFilterColor: (color: string) => void;
  setFilterType: (type: string) => void;
  setFilterProperty: (p: string) => void;
  setFilterRarity: (r: string) => void;
  setGridColumns: (n: number) => void;
  clearDeck: () => void;

  totalCards: () => number;
  isValid: () => boolean;
  getCount: (number: string) => number;
}

export const useDeckStore = create<DeckStore>((set, get) => ({
  leader: null,
  entries: [],
  notice: null,
  searchQuery: "",
  filterColor: "",
  filterType: "",
  filterProperty: "",
  filterRarity: "",
  gridColumns: 8,

  setLeader: (card) => {
    if (!card) {
      set({ leader: null, notice: null });
      return;
    }
    set((s) => {
      const removed = s.entries.reduce(
        (sum, e) => (!colorMatch(card.color, e.card.color) ? sum + e.count : sum),
        0
      );
      const entries = s.entries.filter((e) => colorMatch(card.color, e.card.color));
      return {
        leader: card,
        entries,
        notice: removed > 0
          ? { message: `已自动移除 ${removed} 张颜色不符的卡牌`, type: "info" as const }
          : null,
      };
    });
  },

  addCard: (card) =>
    set((s) => {
      if (s.leader && !colorMatch(s.leader.color, card.color)) {
        return { notice: { message: "无法加入卡组：颜色与领航卡不符", type: "error" as const } };
      }
      const existing = s.entries.find((e) => e.card.number === card.number);
      if (existing) {
        if (existing.count >= 4) return s;
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
  setFilterColor: (color) => set({ filterColor: color }),
  setFilterType: (type) => set({ filterType: type }),
  setFilterProperty: (p) => set({ filterProperty: p }),
  setFilterRarity: (r) => set({ filterRarity: r }),
  setGridColumns: (n) => {
    const clamped = Math.min(MAX_COLS, Math.max(MIN_COLS, n));
    if (typeof window !== "undefined")
      localStorage.setItem(GRID_COLS_KEY, String(clamped));
    set({ gridColumns: clamped });
  },
  clearDeck: () => set({ leader: null, entries: [] }),

  totalCards: () => get().entries.reduce((sum, e) => sum + e.count, 0),

  isValid: () => {
    const s = get();
    return s.leader !== null && s.totalCards() === 40;
  },

  getCount: (number) =>
    get().entries.find((e) => e.card.number === number)?.count ?? 0,
}));
