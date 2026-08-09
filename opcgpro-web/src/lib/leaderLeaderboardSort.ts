import type { LeaderLeaderboardItem } from "@/types/net";

export type LeaderLeaderboardSortKey =
  | "games"
  | "record"
  | "winRate"
  | "usageRate"
  | "firstWinRate"
  | "secondWinRate";

export type LeaderLeaderboardSortDirection = "desc" | "asc";

export interface LeaderLeaderboardSortState {
  key: LeaderLeaderboardSortKey;
  direction: LeaderLeaderboardSortDirection;
}

export function nextLeaderLeaderboardSort(
  current: LeaderLeaderboardSortState | null,
  key: LeaderLeaderboardSortKey,
): LeaderLeaderboardSortState | null {
  if (current?.key !== key) return { key, direction: "desc" };
  if (current.direction === "desc") return { key, direction: "asc" };
  return null;
}

function sortValue(item: LeaderLeaderboardItem, key: LeaderLeaderboardSortKey): number | null {
  switch (key) {
    case "games":
      return item.games;
    case "record":
      return item.wins;
    case "winRate":
      return item.winRate;
    case "usageRate":
      return item.usageRate;
    case "firstWinRate":
      return item.firstWinRate;
    case "secondWinRate":
      return item.secondWinRate;
  }
}

export function sortLeaderLeaderboardItems(
  items: LeaderLeaderboardItem[],
  sort: LeaderLeaderboardSortState | null,
): LeaderLeaderboardItem[] {
  if (!sort) return items;

  return items
    .map((item, index) => ({ item, index, value: sortValue(item, sort.key) }))
    .sort((left, right) => {
      if (left.value == null && right.value == null) return left.index - right.index;
      if (left.value == null) return 1;
      if (right.value == null) return -1;

      const difference = left.value - right.value;
      if (difference === 0) return left.index - right.index;
      return sort.direction === "asc" ? difference : -difference;
    })
    .map(({ item }) => item);
}
