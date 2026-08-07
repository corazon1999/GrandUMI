export interface LeaderBreathingEffect {
  /** 需要启用动效的具体异画路径，避免同一卡号的其他画面被误命中。 */
  sprite: string;
  /** 人物呼吸缩放的视觉中心。 */
  focusX: string;
  focusY: string;
  /** 单次呼吸周期。 */
  duration: string;
  /** 呼吸顶点的缩放比例。 */
  scale: number;
  /** 主体在呼吸顶点向上抬升的距离；使用像素保证小尺寸牌桌上仍清晰可见。 */
  lift: string;
  /** 主体柔边遮罩的中心与半径，用于把人物从扁平卡图中视觉分层。 */
  subjectX: string;
  subjectY: string;
  subjectRadiusX: string;
  subjectRadiusY: string;
  /** 独立于呼吸周期的能量扫光周期，避免动画像机械循环。 */
  energyDuration: string;
  /** 主辉光与辅助辉光的 RGB 分量，供 CSS 透明色复用。 */
  primaryRgb: string;
  secondaryRgb: string;
}

/**
 * 领航首异画呼吸效果配置。
 *
 * 每张异画的人物位置与主色不同，因此使用显式配置控制焦点与色彩；
 * 后续扩展新 Leader 时只需在这里增加一项，不必继续修改渲染组件。
 */
const LEADER_BREATHING_EFFECTS: Record<string, LeaderBreathingEffect> = {
  "OP17-039": {
    sprite: "/cards/op17/OP17-039.png",
    focusX: "66%",
    focusY: "28%",
    duration: "3.6s",
    scale: 1.07,
    lift: "-3px",
    subjectX: "66%",
    subjectY: "38%",
    subjectRadiusX: "53%",
    subjectRadiusY: "57%",
    energyDuration: "7.2s",
    primaryRgb: "34 211 238",
    secondaryRgb: "129 140 248",
  },
};

function normalizeSpritePath(sprite: string): string {
  // 兼容本地路径附带查询参数的情况，同时保留远程图片 URL 的 pathname。
  try {
    return new URL(sprite, "https://grandumi.local").pathname;
  } catch {
    return sprite.split(/[?#]/, 1)[0];
  }
}

/** 仅当卡号和当前画面都匹配配置时返回动效，防止切换到其他异画后仍显示。 */
export function getLeaderBreathingEffect(
  cardNumber: string,
  currentSprite: string,
): LeaderBreathingEffect | null {
  const effect = LEADER_BREATHING_EFFECTS[cardNumber];
  if (!effect) return null;

  return normalizeSpritePath(currentSprite) === normalizeSpritePath(effect.sprite)
    ? effect
    : null;
}
