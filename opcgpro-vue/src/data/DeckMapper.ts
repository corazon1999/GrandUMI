import type { CardData } from "@/types/card";
import { getCard, applyCachedSprite } from "./CardLoader";

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

/** 是否已存在同名卡组 */
export function deckExists(name: string): boolean {
  return Object.prototype.hasOwnProperty.call(loadAllDecks(), name);
}

/** 生成不与已有卡组重名的默认名 */
export function nextDeckName(base = "新卡组"): string {
  const decks = loadAllDecks();
  if (!(base in decks)) return base;
  for (let i = 2; ; i++) {
    const candidate = `${base}${i}`;
    if (!(candidate in decks)) return candidate;
  }
}

export function exportDeckString(leader: CardData, cards: CardData[], name?: string): string {
  const lines: string[] = [];
  lines.push(`# GrandUMI 卡组${name ? ` · ${name}` : ""}`);
  lines.push(`领航: ${leader.number}`);
  const counts = new Map<string, number>();
  const order: string[] = [];
  for (const c of cards) {
    if (!counts.has(c.number)) order.push(c.number);
    counts.set(c.number, (counts.get(c.number) ?? 0) + 1);
  }
  for (const num of order) lines.push(`${counts.get(num)} ${num}`);
  return lines.join("\n");
}

const CARD_NO_RE = /[A-Z]{1,4}\d{0,2}-\d{1,4}/i;

// 「数量x卡号」紧凑格式（OPTCGSim/常见模拟器风格），如 1xOP13-002 3xOP13-007 …，
// 可全部写在一行或换行、空格分隔；x 也接受 × / X，可选 | sprite。领航靠卡牌 type 识别（#159/#160）。
const MULT_RE = /(\d+)\s*[x×]\s*([A-Za-z]{1,4}\d{0,2}-\d{1,4})(?:\s*\|\s*(\S+))?/gi;

export function importDeckString(text: string): { leader: CardData | null; cards: CardData[]; skipped: number } {
  let leader: CardData | null = null;
  const cards: CardData[] = [];
  let skipped = 0;

  // 去掉 # 注释行后整体检测/解析「数量x卡号」紧凑格式
  const cleaned = text
    .split(/\r?\n/)
    .filter((l) => !l.trim().startsWith("#"))
    .join("\n");
  MULT_RE.lastIndex = 0;
  let mm: RegExpExecArray | null;
  let anyMult = false;
  while ((mm = MULT_RE.exec(cleaned)) !== null) {
    anyMult = true;
    const qty = parseInt(mm[1], 10) || 1;
    const number = mm[2].toUpperCase();
    const sprite = mm[3];
    if (sprite) applyCachedSprite(number, sprite);
    const card = getCard(number);
    if (!card) { skipped += qty; continue; }
    if (card.type === "Leader") { leader = card; continue; } // 领航不计入数量
    for (let i = 0; i < Math.min(qty, 4); i++) cards.push(card);
  }
  if (anyMult) return { leader, cards, skipped };

  // ── 回退：格式 A『每行 数量 卡号』──
  for (const rawLine of text.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith("#")) continue;
    const isLeaderLine = /^(领航|leader|l)\s*[:：]/i.test(line);
    const m = line.match(CARD_NO_RE);
    if (!m) continue;
    const number = m[0].toUpperCase();
    const card = getCard(number);
    if (!card) { skipped++; continue; }
    if (isLeaderLine || card.type === "Leader") { leader = card; continue; }
    const beforeNum = line.slice(0, m.index ?? 0);
    const qm = beforeNum.match(/(\d+)/);
    const qty = qm ? Math.min(parseInt(qm[1], 10), 4) : 1;
    for (let i = 0; i < qty; i++) cards.push(card);
  }

  return { leader, cards, skipped };
}
