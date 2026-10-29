import type { StandardRankedCardPool } from "@/types/net";

export const STANDARD_RANKED_CARD_POOLS = [
  { value: "real", label: "实卡环境", subtitle: "EB05 环境池", description: "不使用 OP18、ST37、ST38，只匹配同意实卡环境的对手。" },
  { value: "preview", label: "OP18＋ST37＋ST38", subtitle: "新卡环境", description: "可以使用 OP18、ST37、ST38，也可以使用合法旧卡组，只参与新卡环境匹配。" },
  { value: "all", label: "全环境", subtitle: "兼容两边匹配", description: "参与两种环境匹配，并遵守对手限制；携带新卡时不会匹配实卡环境。" },
] as const;

export function isStandardRankedCardPool(value: unknown): value is StandardRankedCardPool {
  return value === "real" || value === "preview" || value === "all";
}

export function rankedCardPoolStorageKey(account: string): string {
  const normalized = account.trim().toLowerCase();
  return `grandumi_standard_ranked_card_pool_v1:${normalized ? `account:${encodeURIComponent(normalized)}` : "guest"}`;
}

type PoolStorage = Pick<Storage, "getItem" | "setItem">;

export function loadRankedCardPool(account: string, storage: PoolStorage | null): StandardRankedCardPool {
  try {
    const stored = storage?.getItem(rankedCardPoolStorageKey(account));
    return isStandardRankedCardPool(stored) ? stored : "real";
  } catch {
    return "real";
  }
}

export function saveRankedCardPool(account: string, value: StandardRankedCardPool, storage: PoolStorage | null): void {
  try { storage?.setItem(rankedCardPoolStorageKey(account), value); } catch { /* 存储不可用时仍允许本次选择。 */ }
}

/** 同时兼容逐行卡号和保存卡组的展示文本；注释及异画地址不参与判断。 */
export function deckUsesPreviewCards(deck: string, leader?: string): boolean {
  if (/^(?:OP18|ST37|ST38)-\d{1,4}$/i.test(leader?.trim() ?? "")) return true;
  return deck.split(/\r?\n/).some(raw => {
    const line = raw.trim();
    if (!line || line.startsWith("#")) return false;
    const body = line.split("|", 1)[0];
    return /(?:^|[\s:：x×])(?:OP18|ST37|ST38)-\d{1,4}(?=$|\s)/i.test(body);
  });
}
