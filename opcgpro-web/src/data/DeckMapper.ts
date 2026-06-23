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
  /** 异画映射：卡号 → 对应 sprite URL */
  spriteMap: Record<string, string>;
  updatedAt: number;
}

const STORAGE_KEY = "grandumi_decks";

export function saveDeck(
  name: string,
  leader: CardData,
  cards: CardData[]
): void {
  const decks = loadAllDecks();
  // 收集所有卡牌的异画选择（只记录非默认第一个版本的）
  const spriteMap: Record<string, string> = {};
  const allCards = [leader, ...cards];
  for (const c of allCards) {
    if (c.sprites && c.sprites.length > 1 && c.sprite && c.sprite !== c.sprites[0]) {
      spriteMap[c.number] = c.sprite;
    }
  }

  const saved: SavedDeck = {
    name,
    leader: leader.number,
    leaderName: leader.name,
    leaderSprite: leader.sprite ?? "",
    charCount: cards.filter((c) => c.type === "Character").length,
    eventCount: cards.filter((c) => c.type === "Event").length,
    stageCount: cards.filter((c) => c.type === "Stage").length,
    cards: cards.map((c) => c.number),
    spriteMap,
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

/** 是否已存在同名卡组（卡组以名称为唯一 key，保存前查重用） */
export function deckExists(name: string): boolean {
  return Object.prototype.hasOwnProperty.call(loadAllDecks(), name);
}

/** 生成不与已有卡组重名的默认名："新卡组"、"新卡组2"、"新卡组3" … */
export function nextDeckName(base = "新卡组"): string {
  const decks = loadAllDecks();
  if (!(base in decks)) return base;
  for (let i = 2; ; i++) {
    const candidate = `${base}${i}`;
    if (!(candidate in decks)) return candidate;
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

  // 还原异画选择
  if (saved.spriteMap) {
    for (const [num, sprite] of Object.entries(saved.spriteMap)) {
      applyCachedSprite(num, sprite);
    }
  }

  return { leader, cards };
}

/** 获取已保存卡组中某卡号的异画（用于组卡串时带上异画信息） */
export function getSpriteMap(name: string): Record<string, string> {
  const decks = loadAllDecks();
  return decks[name]?.spriteMap ?? {};
}

export function deleteDeck(name: string): void {
  const decks = loadAllDecks();
  delete decks[name];
  localStorage.setItem(STORAGE_KEY, JSON.stringify(decks));
}

function isAltSprite(c: CardData): boolean {
  return !!(c.sprites && c.sprites.length > 1 && c.sprite && c.sprite !== c.sprites[0]);
}

/**
 * 导出为可分享的卡组码文本:
 *   # GrandUMI 卡组 · 名称
 *   领航: OP01-001
 *   4 OP01-016
 *   3 OP01-024 | /sprites/...（异画时附 sprite,| 分隔）
 */
export function exportDeckString(leader: CardData, cards: CardData[], name?: string): string {
  const lines: string[] = [];
  lines.push(`# GrandUMI 卡组${name ? ` · ${name}` : ""}`);
  lines.push(`领航: ${leader.number}${isAltSprite(leader) ? ` | ${leader.sprite}` : ""}`);

  const counts = new Map<string, number>();
  const sprites = new Map<string, string>();
  const order: string[] = [];
  for (const c of cards) {
    if (!counts.has(c.number)) order.push(c.number);
    counts.set(c.number, (counts.get(c.number) ?? 0) + 1);
    if (isAltSprite(c) && !sprites.has(c.number)) sprites.set(c.number, c.sprite!);
  }
  for (const num of order) {
    const sp = sprites.get(num);
    lines.push(`${counts.get(num)} ${num}${sp ? ` | ${sp}` : ""}`);
  }
  return lines.join("\n");
}

const CARD_NO_RE = /[A-Z]{1,4}\d{0,2}-\d{1,4}/i;

// 「数量x卡号」紧凑格式（OPTCGSim/常见模拟器风格），如 1xOP13-002 3xOP13-007 …，
// 可全部写在一行或换行、空格分隔；x 也接受 × / X，可选 | sprite。领航靠卡牌 type 识别（#159/#160）。
const MULT_RE = /(\d+)\s*[x×]\s*([A-Za-z]{1,4}\d{0,2}-\d{1,4})(?:\s*\|\s*(\S+))?/gi;

/**
 * 解析卡组码文本,宽松容错,支持两种格式:
 *   A. 每行『数量 卡号』(导出格式): 领航行以「领航/leader/L:」开头或 type=Leader; 卡号前数字为数量, | 后为异画 sprite。
 *   B. 紧凑『数量x卡号』: 如 1xOP13-002 3xOP13-007 …(可同行/换行)。领航靠 type=Leader 识别。
 * 自动按是否含「数字x卡号」判定走 B, 否则走 A。返回识别到的领航与卡牌(逐张展开)及跳过的无效卡号数量。
 */
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

  // ── 回退:格式 A『每行 数量 卡号』──
  for (const rawLine of text.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith("#")) continue;

    const isLeaderLine = /^(领航|leader|l)\s*[:：]/i.test(line);

    let body = line;
    let spritePart: string | undefined;
    const pipe = line.indexOf("|");
    if (pipe >= 0) {
      spritePart = line.slice(pipe + 1).trim() || undefined;
      body = line.slice(0, pipe).trim();
    }

    const m = body.match(CARD_NO_RE);
    if (!m) continue;
    const number = m[0].toUpperCase();

    if (spritePart) applyCachedSprite(number, spritePart);
    const card = getCard(number);
    if (!card) { skipped++; continue; }

    if (isLeaderLine || card.type === "Leader") {
      leader = card;
      continue;
    }

    const beforeNum = body.slice(0, m.index ?? 0);
    const qm = beforeNum.match(/(\d+)/);
    const qty = qm ? Math.min(parseInt(qm[1], 10), 4) : 1;
    for (let i = 0; i < qty; i++) cards.push(card);
  }

  return { leader, cards, skipped };
}
