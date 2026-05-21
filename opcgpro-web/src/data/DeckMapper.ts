import type { CardData } from "@/types/card";
import { getCard } from "./CardLoader";

export interface SavedDeck {
  name: string;
  leader: string;
  leaderName: string;
  leaderSprite: string;
  charCount: number;
  eventCount: number;
  stageCount: number;
  cards: string[];
  updatedAt: number;
}

const STORAGE_KEY = "grandumi_decks";

export function saveDeck(
  name: string,
  leader: CardData,
  cards: CardData[]
): void {
  const decks = loadAllDecks();
  const saved: SavedDeck = {
    name,
    leader: leader.number,
    leaderName: leader.name,
    leaderSprite: leader.sprite ?? "",
    charCount: cards.filter((c) => c.type === "Character").length,
    eventCount: cards.filter((c) => c.type === "Event").length,
    stageCount: cards.filter((c) => c.type === "Stage").length,
    cards: cards.map((c) => c.number),
    updatedAt: Date.now(),
  };
  decks[name] = saved;
  localStorage.setItem(STORAGE_KEY, JSON.stringify(decks));
}

export function loadAllDecks(): Record<string, SavedDeck> {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? JSON.parse(raw) : {};
  } catch {
    return {};
  }
}

export function loadDeck(
  name: string
): { leader: CardData; cards: CardData[] } | null {
  const decks = loadAllDecks();
  const saved = decks[name];
  if (!saved) return null;

  const leader = getCard(saved.leader);
  if (!leader) return null;

  const cards = saved.cards
    .map((n) => getCard(n))
    .filter((c): c is CardData => c !== undefined);

  return { leader, cards };
}

export function deleteDeck(name: string): void {
  const decks = loadAllDecks();
  delete decks[name];
  localStorage.setItem(STORAGE_KEY, JSON.stringify(decks));
}

export function exportDeckString(leader: CardData, cards: CardData[]): string {
  const lines = [leader.number, ...cards.map((c) => c.number)];
  return lines.join("\n");
}
