import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import {
  installFeedbackShortcut,
  isFeedbackShortcut,
} from "../src/lib/feedbackShortcut.mjs";

function keyboardEvent(overrides = {}) {
  return {
    key: "f",
    code: "KeyF",
    repeat: false,
    ctrlKey: false,
    altKey: false,
    metaKey: false,
    isComposing: false,
    target: { tagName: "DIV", isContentEditable: false },
    defaultPrevented: false,
    preventDefault() {
      this.defaultPrevented = true;
    },
    ...overrides,
  };
}

class FakeWindowTarget {
  listeners = new Map();
  registrations = [];

  addEventListener(type, listener, capture) {
    this.listeners.set(type, listener);
    this.registrations.push({ operation: "add", type, capture });
  }

  removeEventListener(type, listener, capture) {
    if (this.listeners.get(type) === listener) this.listeners.delete(type);
    this.registrations.push({ operation: "remove", type, capture });
  }

  dispatch(type, event) {
    this.listeners.get(type)?.(event);
  }
}

test("F 反馈快捷键在捕获阶段注册并可正常切换与清理", () => {
  const target = new FakeWindowTarget();
  let toggles = 0;
  let closes = 0;
  const uninstall = installFeedbackShortcut(target, {
    onToggle: () => toggles++,
    onClose: () => closes++,
  });

  const event = keyboardEvent();
  target.dispatch("keydown", event);
  assert.equal(toggles, 1);
  assert.equal(event.defaultPrevented, true);
  assert.deepEqual(target.registrations[0], {
    operation: "add",
    type: "keydown",
    capture: true,
  });

  target.dispatch("keydown", keyboardEvent({ key: "Escape", code: "Escape" }));
  assert.equal(closes, 1);

  uninstall();
  assert.equal(target.listeners.has("keydown"), false);
  assert.deepEqual(target.registrations.at(-1), {
    operation: "remove",
    type: "keydown",
    capture: true,
  });
});

test("缺少物理键位 code 时仍识别 F，但不接受其他明确键位", () => {
  assert.equal(isFeedbackShortcut(keyboardEvent({ code: "", key: "F" })), true);
  assert.equal(isFeedbackShortcut(keyboardEvent({ code: "Unidentified", key: "f" })), true);
  assert.equal(isFeedbackShortcut(keyboardEvent({ code: "KeyG", key: "f" })), false);
});

test("输入区域、输入法、组合键、长按和重复按键不会误开反馈", () => {
  for (const overrides of [
    { target: { tagName: "INPUT", isContentEditable: false } },
    { target: { tagName: "textarea", isContentEditable: false } },
    { target: { tagName: "SELECT", isContentEditable: false } },
    { target: { tagName: "SPAN", isContentEditable: true } },
    { isComposing: true },
    { ctrlKey: true },
    { altKey: true },
    { metaKey: true },
    { repeat: true },
  ]) {
    assert.equal(isFeedbackShortcut(keyboardEvent(overrides)), false);
  }
});

test("各路由只挂载一个反馈窗口，避免一次 F 被双重切换抵消", async () => {
  const routes = await Promise.all([
    readFile(new URL("../src/app/game/page.tsx", import.meta.url), "utf8"),
    readFile(new URL("../src/app/home/HomeClient.tsx", import.meta.url), "utf8"),
    readFile(new URL("../src/app/replay/[id]/page.tsx", import.meta.url), "utf8"),
  ]);

  for (const route of routes) {
    assert.equal(route.match(/<FeedbackOverlay\b/g)?.length, 1);
  }
});
