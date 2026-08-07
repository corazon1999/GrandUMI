export interface SavedDeck {
  name: string;
  leader: string;
  leaderName: string;
  leaderSprite: string;
  charCount: number;
  eventCount: number;
  stageCount: number;
  cards: string[];
  /** 异画映射：卡号 → 对应 sprite URL */
  spriteMap: Record<string, string>;
  updatedAt: number;
}
