export const ATTACK_ATTRIBUTES = ["斩", "打", "射", "特", "知", "?"] as const;

export type AttackAttribute = (typeof ATTACK_ATTRIBUTES)[number];

export interface AttackAttributeTheme {
  label: string;
  primary: string;
  secondary: string;
  accent: string;
}

export interface CompositeAttackTheme {
  attributes: AttackAttribute[];
  label: string;
  colors: string[];
  primary: string;
  accent: string;
  isComposite: boolean;
}

export const ATTACK_ATTRIBUTE_THEMES: Record<AttackAttribute, AttackAttributeTheme> = {
  斩: {
    label: "斩",
    primary: "#dffcff",
    secondary: "#38bdf8",
    accent: "#f8fafc",
  },
  打: {
    label: "打",
    primary: "#fb923c",
    secondary: "#facc15",
    accent: "#fff7c2",
  },
  射: {
    label: "射",
    primary: "#fef08a",
    secondary: "#f59e0b",
    accent: "#ffffff",
  },
  特: {
    label: "特",
    primary: "#c084fc",
    secondary: "#f472b6",
    accent: "#f5d0fe",
  },
  知: {
    label: "知",
    primary: "#67e8f9",
    secondary: "#2563eb",
    accent: "#ecfeff",
  },
  "?": {
    label: "?",
    primary: "#e2e8f0",
    secondary: "#22d3ee",
    accent: "#f472b6",
  },
};

const ATTRIBUTE_ALIASES: Record<string, AttackAttribute> = {
  斩: "斩",
  打: "打",
  射: "射",
  特: "特",
  知: "知",
  智: "知",
  "?": "?",
  "？": "?",
  "-": "?",
  "—": "?",
  未知: "?",
  无: "?",
};

/**
 * 将卡牌数据中的单属性、多属性和历史别名统一为六种攻击属性。
 * 多属性按卡面顺序保留并去重，供视觉层逐项叠加，而不是只取主属性。
 */
export function normalizeAttackAttributes(rawProperty: string | null | undefined): AttackAttribute[] {
  const tokens = (rawProperty ?? "")
    .split(/[\/／]/)
    .map((token) => token.trim())
    .filter(Boolean);

  if (tokens.length === 0) return ["?"];

  const normalized: AttackAttribute[] = [];
  for (const token of tokens) {
    const attribute = ATTRIBUTE_ALIASES[token] ?? "?";
    if (!normalized.includes(attribute)) normalized.push(attribute);
  }

  return normalized.length > 0 ? normalized : ["?"];
}

/** 组合全部子属性的颜色序列；第一项控制轨迹骨架，其余项共同参与渐变和命中效果。 */
export function composeAttackTheme(attributes: readonly AttackAttribute[]): CompositeAttackTheme {
  const normalized = attributes.length > 0 ? [...new Set(attributes)] : ["?" as const];
  const themes = normalized.map((attribute) => ATTACK_ATTRIBUTE_THEMES[attribute]);
  const colors = normalized.length === 1
    ? [themes[0].primary, themes[0].secondary, themes[0].accent]
    : themes.flatMap((theme) => [theme.primary, theme.secondary]);

  return {
    attributes: normalized,
    label: normalized.map((attribute) => ATTACK_ATTRIBUTE_THEMES[attribute].label).join("/"),
    colors,
    primary: themes[0].primary,
    accent: themes.at(-1)?.accent ?? themes[0].accent,
    isComposite: normalized.length > 1,
  };
}
