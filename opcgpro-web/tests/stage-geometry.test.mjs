import assert from "node:assert/strict";
import test from "node:test";
import {
  viewportPointToLayer,
  viewportRectToLayerBounds,
} from "../src/lib/stageGeometry.ts";

function rect(left, top, width, height) {
  return {
    left,
    top,
    right: left + width,
    bottom: top + height,
    width,
    height,
  };
}

test("普通布局按各轴缩放还原牌桌坐标", () => {
  const geometry = {
    layerRect: rect(100, 50, 640, 360),
    layerWidth: 1280,
    layerHeight: 720,
    rotateQuarterTurn: false,
  };

  assert.deepEqual(viewportPointToLayer(geometry, { x: 420, y: 230 }), {
    x: 640,
    y: 360,
  });
  assert.deepEqual(viewportRectToLayerBounds(geometry, rect(150, 100, 36, 50.5)), {
    left: 100,
    top: 100,
    width: 72,
    height: 101,
  });
});

test("390×844 竖屏顺时针旋转后正确交换并反转坐标轴", () => {
  const scale = 390 / 720;
  const geometry = {
    layerRect: rect(0, 0, 720 * scale, 1280 * scale),
    layerWidth: 1280,
    layerHeight: 720,
    rotateQuarterTurn: true,
  };

  const center = viewportPointToLayer(geometry, {
    x: 360 * scale,
    y: 640 * scale,
  });
  assert.ok(Math.abs(center.x - 640) < 1e-9);
  assert.ok(Math.abs(center.y - 360) < 1e-9);

  const bounds = viewportRectToLayerBounds(
    geometry,
    rect((720 - 100 - 101) * scale, 100 * scale, 101 * scale, 72 * scale),
  );
  assert.ok(Math.abs(bounds.left - 100) < 1e-9);
  assert.ok(Math.abs(bounds.top - 100) < 1e-9);
  assert.ok(Math.abs(bounds.width - 72) < 1e-9);
  assert.ok(Math.abs(bounds.height - 101) < 1e-9);
});

test("360×780 缩放竖屏仍能还原横置卡牌包围盒", () => {
  const scale = 360 / 390;
  const geometry = {
    layerRect: rect(0, 0, 720 * scale, 1280 * scale),
    layerWidth: 1280,
    layerHeight: 720,
    rotateQuarterTurn: true,
  };
  const cardBounds = { left: 400, top: 250, width: 101, height: 72 };
  const viewportBounds = rect(
    (720 - cardBounds.top - cardBounds.height) * scale,
    cardBounds.left * scale,
    cardBounds.height * scale,
    cardBounds.width * scale,
  );
  const restored = viewportRectToLayerBounds(geometry, viewportBounds);

  assert.ok(Math.abs(restored.left - cardBounds.left) < 1e-9);
  assert.ok(Math.abs(restored.top - cardBounds.top) < 1e-9);
  assert.ok(Math.abs(restored.width - cardBounds.width) < 1e-9);
  assert.ok(Math.abs(restored.height - cardBounds.height) < 1e-9);
});
