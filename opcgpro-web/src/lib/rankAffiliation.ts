import type { RankFaction } from "@/types/net";

export const RANK_AFFILIATION_NAMES: Record<RankFaction, string> = {
  pirate: "海贼", marine: "海军", government: "世界政府",
  east: "东海", west: "西海", south: "南海", north: "北海",
};
export const isHunterAffiliation = (value?: string | null) =>
  value === "east" || value === "west" || value === "south" || value === "north";
export const formatHunterDefeats = (value: number) => value.toLocaleString("zh-CN");

/** 前端热更新兼容旧后端下发的排位与钱包说明。 */
export const normalizeHunterCopy = (text: string) => text
  .replace(/猎人\u4eba\u5934/g, "击败数量")
  .replace(/\u4eba\u5934/g, "击败数量");
