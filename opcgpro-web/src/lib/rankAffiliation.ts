import type { RankFaction } from "@/types/net";

export const RANK_AFFILIATION_NAMES: Record<RankFaction, string> = {
  pirate: "海贼", marine: "海军", government: "世界政府",
  east: "东海", west: "西海", south: "南海", north: "北海",
};
export const isHunterAffiliation = (value?: string | null) =>
  value === "east" || value === "west" || value === "south" || value === "north";
export const formatHunterHeads = (value: number) => `${value.toLocaleString("zh-CN")} 人头`;
