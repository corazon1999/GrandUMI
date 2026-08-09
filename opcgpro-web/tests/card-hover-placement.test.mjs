import assert from "node:assert/strict";
import test from "node:test";
import { calculateCardHoverPlacement } from "../src/lib/cardHoverPlacement.ts";

const sourceRect = { left: 120, right: 180, top: 400, height: 80 };

test("竖屏自动横屏时悬停详情旋转后缩放到可视区域内", () => {
  const placement = calculateCardHoverPlacement({
    rect: sourceRect,
    viewportWidth: 390,
    viewportHeight: 844,
    previewWidth: 240,
    previewHeight: 480,
    rotateQuarterTurn: true,
  });

  assert.equal(placement.scale, 374 / 480);
  assert.equal(placement.footprintWidth, 374);
  assert.equal(placement.footprintHeight, 187);
  assert.equal(placement.left, 8);
  assert.ok(placement.top >= 8);
  assert.ok(placement.top + placement.footprintHeight <= 836);
});

test("普通布局保持竖向详情并优先显示在卡牌右侧", () => {
  const placement = calculateCardHoverPlacement({
    rect: sourceRect,
    viewportWidth: 1280,
    viewportHeight: 800,
    previewWidth: 240,
    previewHeight: 480,
    rotateQuarterTurn: false,
  });

  assert.deepEqual(placement, {
    left: 192,
    top: 200,
    footprintWidth: 240,
    footprintHeight: 480,
    scale: 1,
    showRight: true,
  });
});
