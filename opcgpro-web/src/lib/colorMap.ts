// 卡牌颜色（红绿蓝紫黑黄）的样式与显示工具
// 数据层已统一为标准色（红/绿/蓝/紫/黑/黄），不再需要元素色↔显示色映射

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

/** 卡牌颜色字符串（如 "红/绿"）按原样返回（数据层已是显示色，保留函数以兼容调用点） */
export function toDisplayColor(dataColor: string): string {
  return dataColor;
}

/** 获取卡牌颜色的首个色名（用于取样式） */
export function primaryDisplayColor(dataColor: string): string {
  return dataColor.split("/")[0].trim();
}
