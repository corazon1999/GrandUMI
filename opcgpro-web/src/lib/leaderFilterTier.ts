import type { LeaderFilterTier } from "@/types/net";

export const LEADER_FILTER_TIER_STORAGE_KEY = "grandumi_leader_filter_tier";
export const DEFAULT_LEADER_FILTER_TIER: LeaderFilterTier = "standard";

export function getLeaderFilterTierStorage(): Storage | null {
  if (typeof window === "undefined") return null;
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}

export function normalizeLeaderFilterTier(value: unknown): LeaderFilterTier {
  return value === "relaxed" || value === "all" ? value : DEFAULT_LEADER_FILTER_TIER;
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
