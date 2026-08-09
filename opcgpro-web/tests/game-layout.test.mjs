import assert from "node:assert/strict";
import test from "node:test";
import { calculateLayoutScale, resolveGameLayout } from "../src/lib/gameLayout.ts";
import {
  LAYOUT_CANVAS_SIZES,
  LAYOUT_PREVIEW_OPTIONS,
  normalizeStoredLayoutPreviewMode,
} from "../src/lib/layoutSettings.ts";

test("布局设置只提供电脑和手机竖屏", () => {
  assert.deepEqual(
    LAYOUT_PREVIEW_OPTIONS.map((option) => option.value),
    ["desktop", "mobile-portrait"],
  );
  assert.deepEqual(LAYOUT_CANVAS_SIZES["mobile-landscape"], { width: 844, height: 390 });
});

test("旧手机横屏设置自动迁移为电脑布局", () => {
  assert.equal(normalizeStoredLayoutPreviewMode("mobile-landscape"), "desktop");
  assert.equal(normalizeStoredLayoutPreviewMode("mobile-portrait"), "mobile-portrait");
  assert.equal(normalizeStoredLayoutPreviewMode("desktop"), "desktop");
});

test("真实手机竖屏会使用铺满屏幕的旋转横屏画布", () => {
  assert.deepEqual(resolveGameLayout("desktop", true), {
    mode: "mobile-landscape",
    rotateQuarterTurn: true,
    edgeToEdge: true,
  });
});

test("手动手机竖屏只在对局路由转换为旋转横屏预览", () => {
  assert.deepEqual(resolveGameLayout("mobile-portrait", false), {
    mode: "mobile-landscape",
    rotateQuarterTurn: true,
    edgeToEdge: false,
  });
});

test("旋转后按交换宽高的视觉占位计算缩放", () => {
  assert.equal(calculateLayoutScale({
    hostWidth: 390,
    hostHeight: 844,
    canvasWidth: 844,
    canvasHeight: 390,
    rotateQuarterTurn: true,
    edgeToEdge: true,
  }), 1);

  assert.equal(calculateLayoutScale({
    hostWidth: 360,
    hostHeight: 780,
    canvasWidth: 844,
    canvasHeight: 390,
    rotateQuarterTurn: true,
    edgeToEdge: true,
  }), 360 / 390);
});
