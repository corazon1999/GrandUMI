// 卡牌颜色（红绿蓝紫黑黄）的样式与显示工具
// 数据层已统一为标准色（红/绿/蓝/紫/黑/黄），不再需要元素色↔显示色映射

// UI 显示顺序
export const COLOR_DISPLAY_NAMES = ["红", "绿", "蓝", "紫", "黑", "黄"] as const;
export type ColorDisplayName = (typeof COLOR_DISPLAY_NAMES)[number];
const COLOR_DISPLAY_NAME_SET = new Set<string>(COLOR_DISPLAY_NAMES);

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

/**
 * 从卡牌颜色字段中提取规则色。
 * 历史导入可能使用半角/全角斜杠、逗号或“紫色黑色”等排版；规则只认可六种标准色。
 */
export function parseCardColors(dataColor: string | null | undefined): ColorDisplayName[] {
  if (!dataColor) return [];
  const colors: ColorDisplayName[] = [];
  for (const char of dataColor.normalize("NFKC")) {
    if (!COLOR_DISPLAY_NAME_SET.has(char) || colors.includes(char as ColorDisplayName)) continue;
    colors.push(char as ColorDisplayName);
  }
  return colors;
}

/** 两张卡只要共享任一规则色即为颜色兼容。 */
export function sharesCardColor(left: string, right: string): boolean {
  const leftColors = new Set(parseCardColors(left));
  return parseCardColors(right).some((color) => leftColors.has(color));
}

/** 获取卡牌颜色的首个色名（用于取样式） */
export function primaryDisplayColor(dataColor: string): string {
  return parseCardColors(dataColor)[0] ?? dataColor.trim();
}
