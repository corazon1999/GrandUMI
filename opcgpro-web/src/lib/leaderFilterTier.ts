import type { LeaderFilterTier } from "@/types/net";

export const LEADER_FILTER_TIER_STORAGE_KEY = "grandumi_leader_filter_tier";
export const DEFAULT_LEADER_FILTER_TIER: LeaderFilterTier = "500";

const LEADER_FILTER_TIERS = new Set<LeaderFilterTier>(["100", "300", "500", "1000", "3000", "all"]);

export function getLeaderFilterTierStorage(): Storage | null {
  if (typeof window === "undefined") return null;
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}

export function normalizeLeaderFilterTier(value: unknown): LeaderFilterTier {
  if (typeof value !== "string") return DEFAULT_LEADER_FILTER_TIER;
  const normalized = value.trim().toLowerCase();
  if (LEADER_FILTER_TIERS.has(normalized as LeaderFilterTier)) return normalized as LeaderFilterTier;
  // 旧版把同一档位随周期解释为 100 / 300 或 500 / 3000；迁移到固定档时按默认 7 天口径保留选择。
  if (normalized === "relaxed") return "100";
  if (normalized === "standard") return "500";
  return DEFAULT_LEADER_FILTER_TIER;
}

export function readLeaderFilterTier(storage: Pick<Storage, "getItem"> | null | undefined): LeaderFilterTier {
  if (!storage) return DEFAULT_LEADER_FILTER_TIER;
  try {
    return normalizeLeaderFilterTier(storage.getItem(LEADER_FILTER_TIER_STORAGE_KEY));
  } catch {
    return DEFAULT_LEADER_FILTER_TIER;
  }
}

export function writeLeaderFilterTier(
  storage: Pick<Storage, "setItem"> | null | undefined,
  filterTier: LeaderFilterTier,
): void {
  if (!storage) return;
  try {
    storage.setItem(LEADER_FILTER_TIER_STORAGE_KEY, normalizeLeaderFilterTier(filterTier));
  } catch {
    // 隐私模式或存储配额异常不应阻断榜单筛选。
  }
}
