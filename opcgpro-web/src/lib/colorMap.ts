// 颜色映射：显示名（红绿蓝紫黑黄）↔ 卡牌数据值（炎风水暗地光）

export const COLOR_DATA_TO_DISPLAY: Record<string, string> = {
  炎: "红",
  风: "绿",
  水: "蓝",
  暗: "紫",
  地: "黑",
  光: "黄",
};

export const COLOR_DISPLAY_TO_DATA: Record<string, string> = {
  红: "炎",
  绿: "风",
  蓝: "水",
  紫: "暗",
  黑: "地",
  黄: "光",
};

// UI 显示顺序
export const COLOR_DISPLAY_NAMES = ["红", "绿", "蓝", "紫", "黑", "黄"] as const;
export type ColorDisplayName = (typeof COLOR_DISPLAY_NAMES)[number];

// 每种颜色对应的 Tailwind 样式
export const COLOR_STYLES: Record<string, { bg: string; text: string; border: string }> = {
  红: { bg: "bg-red-600",    text: "text-red-400",    border: "border-red-500"    },
  绿: { bg: "bg-green-600",  text: "text-green-400",  border: "border-green-500"  },
  蓝: { bg: "bg-blue-600",   text: "text-blue-400",   border: "border-blue-500"   },
  紫: { bg: "bg-purple-600", text: "text-purple-400", border: "border-purple-500" },
  黑: { bg: "bg-gray-600",   text: "text-gray-400",   border: "border-gray-500"   },
  黄: { bg: "bg-yellow-500", text: "text-yellow-400", border: "border-yellow-400" },
};

/** 将数据中的颜色字符串（如 "炎/风"）转为显示名（如 "红/绿"） */
export function toDisplayColor(dataColor: string): string {
  return dataColor
    .split("/")
    .map((c) => COLOR_DATA_TO_DISPLAY[c.trim()] ?? c.trim())
    .join("/");
}

/** 获取卡牌颜色对应的首个显示名（用于取样式） */
export function primaryDisplayColor(dataColor: string): string {
  const first = dataColor.split("/")[0].trim();
  return COLOR_DATA_TO_DISPLAY[first] ?? first;
}
