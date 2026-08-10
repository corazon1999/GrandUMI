export interface ViewportPoint {
  x: number;
  y: number;
}

export interface ViewportRect {
  left: number;
  top: number;
  right: number;
  bottom: number;
  width: number;
  height: number;
}

export interface LayerBounds {
  left: number;
  top: number;
  width: number;
  height: number;
}

interface LayerGeometry {
  layerRect: ViewportRect;
  layerWidth: number;
  layerHeight: number;
  rotateQuarterTurn: boolean;
}

/**
 * 将浏览器视口坐标还原为固定牌桌画布坐标。
 * 竖屏对局由外层顺时针旋转 90°，此时画布的 X 轴对应视口 Y 轴，
 * 画布的 Y 轴对应反向视口 X 轴，不能继续使用普通缩放公式。
 */
export function viewportPointToLayer(
  geometry: LayerGeometry,
  point: ViewportPoint,
): ViewportPoint {
  const {
    layerRect,
    layerWidth,
    layerHeight,
    rotateQuarterTurn,
  } = geometry;

  if (rotateQuarterTurn) {
    const scaleX = layerRect.height / layerWidth || 1;
    const scaleY = layerRect.width / layerHeight || 1;
    return {
      x: (point.y - layerRect.top) / scaleX,
      y: (layerRect.right - point.x) / scaleY,
    };
  }

  const scaleX = layerRect.width / layerWidth || 1;
  const scaleY = layerRect.height / layerHeight || 1;
  return {
    x: (point.x - layerRect.left) / scaleX,
    y: (point.y - layerRect.top) / scaleY,
  };
}

/** 将视口中的轴对齐包围盒转换为牌桌画布内的轴对齐包围盒。 */
export function viewportRectToLayerBounds(
  geometry: LayerGeometry,
  rect: ViewportRect,
): LayerBounds {
  const corners = [
    viewportPointToLayer(geometry, { x: rect.left, y: rect.top }),
    viewportPointToLayer(geometry, { x: rect.right, y: rect.top }),
    viewportPointToLayer(geometry, { x: rect.right, y: rect.bottom }),
    viewportPointToLayer(geometry, { x: rect.left, y: rect.bottom }),
  ];
  const xs = corners.map((point) => point.x);
  const ys = corners.map((point) => point.y);
  const left = Math.min(...xs);
  const right = Math.max(...xs);
  const top = Math.min(...ys);
  const bottom = Math.max(...ys);

  return {
    left,
    top,
    width: right - left,
    height: bottom - top,
  };
}
