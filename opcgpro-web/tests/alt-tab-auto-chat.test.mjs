import test from "node:test";
import assert from "node:assert/strict";

import {
  ALT_TAB_AUTO_CHAT_MESSAGE,
  installAltTabAutoChat,
  isAltTabAutoChatEligible,
} from "../src/lib/altTabAutoChat.ts";

class FakeDocument extends EventTarget {
  visibilityState = "visible";

  setVisibility(visibilityState) {
    this.visibilityState = visibilityState;
    this.dispatchEvent(new Event("visibilitychange"));
  }
}

function keyboardEvent(type, init = {}) {
  const event = new Event(type);
  Object.assign(event, {
    key: "",
    altKey: false,
    ctrlKey: false,
    metaKey: false,
    repeat: false,
  }, init);
  return event;
}

function setup({ canSend = () => true } = {}) {
  const windowTarget = new EventTarget();
  const documentTarget = new FakeDocument();
  const messages = [];
  let time = 1_000;
  const cleanup = installAltTabAutoChat({
    windowTarget,
    documentTarget,
    canSend,
    send: (message) => messages.push(message),
    now: () => time,
  });

  return {
    windowTarget,
    documentTarget,
    messages,
    cleanup,
    advance: (milliseconds) => { time += milliseconds; },
  };
}

function pressAltTab(windowTarget) {
  windowTarget.dispatchEvent(keyboardEvent("keydown", { key: "Tab", altKey: true }));
}

test("Alt+Tab 后页面隐藏只发送一次固定局内消息", () => {
  const context = setup();
  pressAltTab(context.windowTarget);
  context.documentTarget.setVisibility("hidden");
  context.windowTarget.dispatchEvent(new Event("blur"));
  context.documentTarget.dispatchEvent(new Event("visibilitychange"));

  assert.deepEqual(context.messages, [ALT_TAB_AUTO_CHAT_MESSAGE]);
  assert.equal(context.messages[0], "老板来了，等我一会");
  context.cleanup();
});

test("单独 Alt、普通 Tab、单独失焦和手机式页面隐藏都不发送", () => {
  const context = setup();
  context.windowTarget.dispatchEvent(keyboardEvent("keydown", { key: "Alt", altKey: true }));
  context.windowTarget.dispatchEvent(keyboardEvent("keydown", { key: "Tab" }));
  context.windowTarget.dispatchEvent(new Event("blur"));
  context.documentTarget.setVisibility("hidden");

  assert.deepEqual(context.messages, []);
  context.cleanup();
});

test("恢复页面后下一次 Alt+Tab 可以再次发送", () => {
  const context = setup();
  pressAltTab(context.windowTarget);
  context.windowTarget.dispatchEvent(new Event("blur"));
  context.documentTarget.setVisibility("hidden");
  assert.equal(context.messages.length, 1);

  context.documentTarget.setVisibility("visible");
  context.windowTarget.dispatchEvent(new Event("focus"));
  pressAltTab(context.windowTarget);
  context.windowTarget.dispatchEvent(new Event("blur"));

  assert.deepEqual(context.messages, [ALT_TAB_AUTO_CHAT_MESSAGE, ALT_TAB_AUTO_CHAT_MESSAGE]);
  context.cleanup();
});

test("观战、回放或已经结算等无效身份不会发送", () => {
  for (const state of [
    { mode: "Observer", viewerKind: "spectator", isGameOver: false, my: {}, opponent: {} },
    { mode: "Playback", viewerKind: "player", isGameOver: false, my: {}, opponent: {} },
    { mode: "Player", viewerKind: "player", isGameOver: true, my: {}, opponent: {} },
    { mode: "Player", viewerKind: "player", isGameOver: false, my: null, opponent: null },
  ]) {
    const context = setup({
      canSend: () => isAltTabAutoChatEligible(state),
    });
    pressAltTab(context.windowTarget);
    context.documentTarget.setVisibility("hidden");
    assert.deepEqual(context.messages, [], `无效状态不应发送：${JSON.stringify(state)}`);
    context.cleanup();
  }
});

test("有效的当前对局玩家身份允许发送", () => {
  assert.equal(isAltTabAutoChatEligible({
    mode: "Player",
    viewerKind: "player",
    isGameOver: false,
    my: {},
    opponent: {},
  }), true);
});

test("过期意图与卸载后的事件不会发送", () => {
  const context = setup();
  pressAltTab(context.windowTarget);
  context.advance(1_501);
  context.windowTarget.dispatchEvent(new Event("blur"));
  assert.deepEqual(context.messages, []);

  context.cleanup();
  pressAltTab(context.windowTarget);
  context.documentTarget.setVisibility("hidden");
  assert.deepEqual(context.messages, []);
});
