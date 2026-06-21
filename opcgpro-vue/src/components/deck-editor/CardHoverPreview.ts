import type { CardData } from "@/types/card";

export const PREVIEW_W = 240;

export interface HoverInfo {
  card: CardData;
  rect: DOMRect;
  currentSprite: string;
}

export const RARITY_STYLES: Record<string, string> = {
  L: "bg-yellow-500 text-black",
  SR: "bg-pink-500 text-white",
  R: "bg-sky-500 text-white",
  UC: "bg-gray-500 text-white",
  U: "bg-gray-500 text-white",
  C: "bg-gray-700 text-gray-300",
  SEC: "bg-red-600 text-white",
  P: "bg-emerald-500 text-white",
};
