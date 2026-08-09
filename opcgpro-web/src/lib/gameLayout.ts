import type {
  LayoutPreviewMode,
  SelectableLayoutPreviewMode,
} from "@/lib/layoutSettings";

export interface GameLayoutResolution {
  mode: LayoutPreviewMode;
  rotateQuarterTurn: boolean;
  edgeToEdge: boolean;
}

/**
 * 真实手机竖屏或手动选择手机竖屏时，对局使用横屏画布并顺时针旋转 90°。
 * 大厅仍保留原始布局设置，只有对局/回放路由使用该解析结果。
 */
export function resolveGameLayout(
  mode: SelectableLayoutPreviewMode,
  isPhonePortrait: boolean,
): GameLayoutResolution {
  const rotateQuarterTurn = isPhonePortrait || mode === "mobile-portrait";
  if (rotateQuarterTurn) {
    return {
      mode: "mobile-landscape",
      rotateQuarterTurn: true,
      edgeToEdge: isPhonePortrait,
    };
  }

  return { mode, rotateQuarterTurn: false, edgeToEdge: false };
}

export function calculateLayoutScale({
  hostWidth,
  hostHeight,
  canvasWidth,
  canvasHeight,
  rotateQuarterTurn,
  edgeToEdge,
}: {
  hostWidth: number;
  hostHeight: number;
  canvasWidth: number;
  canvasHeight: number;
  rotateQuarterTurn: boolean;
  edgeToEdge: boolean;
}) {
  const margin = edgeToEdge ? 0 : 32;
  const availableWidth = Math.max(1, hostWidth - margin);
  const availableHeight = Math.max(1, hostHeight - margin);
  const footprintWidth = rotateQuarterTurn ? canvasHeight : canvasWidth;
  const footprintHeight = rotateQuarterTurn ? canvasWidth : canvasHeight;

  return Math.min(1, availableWidth / footprintWidth, availableHeight / footprintHeight);
}
