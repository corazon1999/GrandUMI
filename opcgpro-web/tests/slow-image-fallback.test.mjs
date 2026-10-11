import assert from "node:assert/strict";
import test from "node:test";
import { watchSlowImage, SLOW_IMAGE_FALLBACK_MS } from "../src/lib/slowImageFallback.ts";

function fixture({ loaded = false, observer = true } = {}) {
  const image = Object.assign(new EventTarget(), {
    src: "https://assets.grand-umi.com/cards-thumb/st38/ST38-001.webp",
    complete: loaded,
    naturalWidth: loaded ? 128 : 0,
    isConnected: true,
  });
  let timer, visible, count = 0;
  const cleanup = watchSlowImage({
    image,
    source: image.src,
    onSlow: () => count++,
    schedule: (callback, delay) => { timer = { callback, delay }; return 1; },
    cancel: () => { timer = undefined; },
    createObserver: observer
      ? (callback) => { visible = callback; return { observe() {}, disconnect() {} }; }
      : undefined,
  });
  return { image, cleanup, get count() { return count; }, get timer() { return timer; },
    enter: () => visible?.([{ isIntersecting: true }]),
    leave: () => visible?.([{ isIntersecting: false }]),
    fire: () => timer?.callback(),
  };
}

test("离屏懒加载图片不会提前启动回退下载", () => {
  const f = fixture();
  f.leave();
  assert.equal(f.timer, undefined);
  f.enter();
  assert.equal(f.timer.delay, SLOW_IMAGE_FALLBACK_MS);
  f.fire();
  assert.equal(f.count, 1);
  f.enter();
  f.fire();
  assert.equal(f.count, 1);
});

test("图片正常加载或报错后会取消慢请求计时", () => {
  for (const event of ["load", "error"]) {
    const f = fixture();
    f.enter();
    const callback = f.timer.callback;
    f.image.dispatchEvent(new Event(event));
    callback();
    assert.equal(f.count, 0);
  }
});

test("换卡、卸载和断开的节点不会被旧计时器切换图片", () => {
  for (const invalidate of [f => { f.image.src += "?v=另一张图"; }, f => f.cleanup(), f => { f.image.isConnected = false; }]) {
    const f = fixture();
    f.enter();
    const callback = f.timer.callback;
    invalidate(f);
    callback();
    assert.equal(f.count, 0);
  }
});

test("已命中缓存或刚完成加载的图片不会再回退", () => {
  const loaded = fixture({ loaded: true });
  loaded.enter();
  assert.equal(loaded.timer, undefined);
  const f = fixture();
  f.enter();
  f.image.complete = true;
  f.image.naturalWidth = 128;
  f.fire();
  assert.equal(f.count, 0);
});

test("不支持可见性观察的浏览器仍可一次回退", () => {
  const f = fixture({ observer: false });
  assert.equal(f.timer.delay, 2000);
  f.fire();
  assert.equal(f.count, 1);
});
