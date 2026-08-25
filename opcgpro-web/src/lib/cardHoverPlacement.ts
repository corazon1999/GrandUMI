export interface CardHoverPlacementInput {
  rect: Pick<DOMRect, "left" | "right" | "top" | "height">;
  viewportWidth: number;
  viewportHeight: number;
  previewWidth: number;
  previewHeight: number;
  rotateQuarterTurn: boolean;
  gap?: number;
  margin?: number;
}

export interface CardHoverPlacement {
  left: number;
  top: number;
  footprintWidth: number;
  footprintHeight: number;
  scale: number;
  showRight: boolean;
}

/** PC 鼠标悬停详情使用的大图尺寸；触摸和手写笔不触发该预览。 */
export const DESKTOP_CARD_HOVER_PREVIEW_WIDTH = 300;
export const DESKTOP_CARD_HOVER_PREVIEW_HEIGHT_APPROX = 560;

export function shouldShowDesktopCardHoverPreview(pointerType: string): boolean {
  return pointerType === "mouse";
}

function clamp(value: number, min: number, max: number) {
  return Math.max(min, Math.min(value, max));
}

export function calculateCardHoverPlacement({
  rect,
  viewportWidth,
  viewportHeight,
  previewWidth,
  previewHeight,
  rotateQuarterTurn,
  gap = 12,
  margin = 8,
}: CardHoverPlacementInput): CardHoverPlacement {
  const rawFootprintWidth = rotateQuarterTurn ? previewHeight : previewWidth;
  const rawFootprintHeight = rotateQuarterTurn ? previewWidth : previewHeight;
  const availableWidth = Math.max(1, viewportWidth - margin * 2);
  const availableHeight = Math.max(1, viewportHeight - margin * 2);
  const scale = Math.min(
    1,
    availableWidth / rawFootprintWidth,
    availableHeight / rawFootprintHeight,
  );
  const footprintWidth = rawFootprintWidth * scale;
  const footprintHeight = rawFootprintHeight * scale;

  const spaceRight = viewportWidth - rect.right;
  const spaceLeft = rect.left;
  const showRight =
    spaceRight >= footprintWidth + gap + margin || spaceRight >= spaceLeft;
  const rawLeft = showRight
    ? rect.right + gap
    : rect.left - gap - footprintWidth;
  const maxLeft = Math.max(margin, viewportWidth - footprintWidth - margin);
  const left = clamp(rawLeft, margin, maxLeft);

  const cardCenterY = rect.top + rect.height / 2;
  const rawTop = cardCenterY - footprintHeight / 2;
  const maxTop = Math.max(margin, viewportHeight - footprintHeight - margin);
  const top = clamp(rawTop, margin, maxTop);

  return {
    left,
    top,
    footprintWidth,
    footprintHeight,
    scale,
    showRight,
  };
}
