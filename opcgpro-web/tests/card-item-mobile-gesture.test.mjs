import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import {
  CARD_LONG_PRESS_DELAY_MS,
  CARD_LONG_PRESS_MOVE_THRESHOLD_PX,
  createCardLongPressGesture,
} from "../src/lib/cardLongPressGesture.ts";
import { calculateLayoutScale } from "../src/lib/gameLayout.ts";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

class FakeScheduler {
  currentTime = 0;
  nextId = 1;
  tasks = new Map();

  now = () => this.currentTime;

  setTimeout = (callback, delayMs) => {
    const id = this.nextId++;
    this.tasks.set(id, { callback, runAt: this.currentTime + delayMs });
    return id;
  };

  clearTimeout = (id) => {
    this.tasks.delete(id);
  };

  advance(ms) {
    const target = this.currentTime + ms;
    while (true) {
      const next = [...this.tasks.entries()]
        .filter(([, task]) => task.runAt <= target)
        .sort((first, second) => first[1].runAt - second[1].runAt)[0];
      if (!next) break;
      const [id, task] = next;
      this.tasks.delete(id);
      this.currentTime = task.runAt;
      task.callback();
    }
    this.currentTime = target;
  }
}

const point = (pointerId = 1, clientX = 100, clientY = 200) => ({
  pointerId,
  clientX,
  clientY,
});

function setupGesture() {
  const scheduler = new FakeScheduler();
  const opened = [];
  const gesture = createCardLongPressGesture({
    scheduler,
    onLongPress: (identity) => opened.push(identity),
  });
  return { scheduler, opened, gesture };
}

test("499ms 释放保持原短按操作，500ms 静止长按只打开详情", () => {
  const shortPress = setupGesture();
  shortPress.gesture.start(point(), "card-a");
  shortPress.scheduler.advance(CARD_LONG_PRESS_DELAY_MS - 1);
  assert.deepEqual(shortPress.opened, []);
  assert.equal(shortPress.gesture.finish(point()), "short-press");
  assert.equal(shortPress.gesture.consumeSuppressedClick(point()), false);

  let cardActionCount = 0;
  let ancestorClickCount = 0;
  cardActionCount += 1;
  ancestorClickCount += 1;
  assert.equal(cardActionCount, 1);
  assert.equal(ancestorClickCount, 1);

  const longPress = setupGesture();
  longPress.gesture.start(point(), "card-a");
  longPress.scheduler.advance(CARD_LONG_PRESS_DELAY_MS);
  assert.deepEqual(longPress.opened, ["card-a"]);
  assert.equal(longPress.gesture.finish(point()), "long-press");

  // 模拟窗口捕获阶段：合成 click 被消费后，卡牌动作和祖先 click 都不会执行。
  if (!longPress.gesture.consumeSuppressedClick(point())) {
    cardActionCount += 1;
    ancestorClickCount += 1;
  }
  assert.equal(cardActionCount, 1);
  assert.equal(ancestorClickCount, 1);
});

test("移动达到 8px 会取消长按，HandArea 捕获后的窗口移动仍可调用同一判定", () => {
  const { scheduler, opened, gesture } = setupGesture();
  gesture.start(point(), "card-a");
  assert.equal(
    gesture.move(point(1, 100 + CARD_LONG_PRESS_MOVE_THRESHOLD_PX, 200)),
    true,
  );
  assert.equal(gesture.hasActivePress(), false);
  scheduler.advance(CARD_LONG_PRESS_DELAY_MS);
  assert.deepEqual(opened, []);
});

test("触摸 contextmenu 不重复打开，真实鼠标右键即使紧随触摸也可用", () => {
  const { scheduler, opened, gesture } = setupGesture();
  gesture.start(point(), "card-a");
  scheduler.advance(CARD_LONG_PRESS_DELAY_MS);
  assert.deepEqual(opened, ["card-a"]);
  assert.equal(gesture.shouldSuppressContextMenu(point()), true);

  gesture.noteMousePointerDown(point(), 2);
  assert.equal(gesture.shouldSuppressContextMenu(point()), false);
});

test("长按松手后合成点击抑制维持约 1000ms，随后自动失效", () => {
  const { scheduler, gesture } = setupGesture();
  gesture.start(point(), "card-a");
  scheduler.advance(CARD_LONG_PRESS_DELAY_MS);
  assert.equal(gesture.finish(point()), "long-press");
  scheduler.advance(999);
  assert.equal(gesture.consumeSuppressedClick(point(1, 140, 240)), false, "远处点击不应误拦截");
  assert.equal(gesture.consumeSuppressedClick(point()), true);

  gesture.start(point(), "card-a");
  scheduler.advance(CARD_LONG_PRESS_DELAY_MS);
  assert.equal(gesture.finish(point()), "long-press");
  scheduler.advance(1_001);
  assert.equal(gesture.consumeSuppressedClick(point()), false);
});

test("pointercancel、卡牌身份变化清理和组件卸载都会取消未完成计时器", () => {
  const cancelled = setupGesture();
  cancelled.gesture.start(point(), "card-a");
  cancelled.scheduler.advance(CARD_LONG_PRESS_DELAY_MS - 1);
  assert.equal(cancelled.gesture.cancelPointer(point()), "short-press");
  cancelled.scheduler.advance(1);
  assert.deepEqual(cancelled.opened, []);

  const identityChanged = setupGesture();
  identityChanged.gesture.start(point(), "card-a");
  identityChanged.scheduler.advance(CARD_LONG_PRESS_DELAY_MS - 1);
  identityChanged.gesture.cancelActive();
  identityChanged.scheduler.advance(1);
  assert.deepEqual(identityChanged.opened, []);

  const unmounted = setupGesture();
  unmounted.gesture.start(point(), "card-a");
  unmounted.scheduler.advance(CARD_LONG_PRESS_DELAY_MS - 1);
  unmounted.gesture.dispose();
  unmounted.scheduler.advance(1);
  assert.deepEqual(unmounted.opened, []);
});

test("CardItem 仅允许鼠标悬停，并在窗口捕获阶段拦截长按后的祖先点击", async () => {
  const source = await readSource("../src/components/ui/CardItem.tsx");

  assert.match(source, /if \(e\.pointerType !== "mouse"/);
  assert.doesNotMatch(source, /onMouseEnter=/);
  assert.match(source, /window\.addEventListener\("pointermove", handleWindowPointerMove, true\)/);
  assert.match(source, /window\.addEventListener\("pointerup", handleWindowPointerUp, true\)/);
  assert.match(source, /window\.addEventListener\("click", handleWindowClick, true\)/);
  assert.match(source, /event\.preventDefault\(\);\s+event\.stopImmediatePropagation\(\);/);
  assert.match(source, /onClick=\{handleClick\}/);
  assert.doesNotMatch(source, /onClick=\{onClick\}/);
  assert.match(source, /e\.preventDefault\(\);\s+e\.stopPropagation\(\);\s+e\.nativeEvent\.stopImmediatePropagation\(\);/);
  assert.match(source, /style=\{\{ WebkitTouchCallout: "none" \}\}/);
  assert.match(source, /zoomCardIdentity === cardIdentity[\s\S]*?!showFaceDown/);
  assert.match(source, /longPressGesture\.cancelActive\(\);\s+clearHoverPreview\(\);\s+setZoomCardIdentity\(null\);/);
});

test("390×844 与 360×780 自动旋转后关闭按钮实际触控区域仍至少 44px", async () => {
  const source = await readSource("../src/components/ui/CardZoomOverlay.tsx");
  const designTargetSize = 48;
  const viewports = [
    { width: 390, height: 844 },
    { width: 360, height: 780 },
  ];

  for (const viewport of viewports) {
    const scale = calculateLayoutScale({
      hostWidth: viewport.width,
      hostHeight: viewport.height,
      canvasWidth: 844,
      canvasHeight: 390,
      rotateQuarterTurn: true,
      edgeToEdge: true,
    });
    assert.ok(designTargetSize * scale >= 44, `${viewport.width}×${viewport.height} 触控区不足 44px`);
  }

  assert.match(source, /min-h-12 min-w-12/);
  assert.match(source, /var\(--layout-safe-right/);
  assert.match(source, /var\(--layout-safe-top/);
  assert.match(source, /100cqh/);
  assert.match(source, /100cqw/);
});
