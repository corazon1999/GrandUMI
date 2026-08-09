import assert from "node:assert/strict";
import test from "node:test";
import {
  DEFAULT_LOCALE,
  LANGUAGE_OPTIONS,
  MESSAGES,
  isSupportedLocale,
  translateText,
} from "../src/i18n/core.mjs";

test("supports Simplified Chinese, Japanese, and English", () => {
  assert.equal(DEFAULT_LOCALE, "zh-CN");
  assert.deepEqual(
    LANGUAGE_OPTIONS.map((option) => option.value),
    ["zh-CN", "ja", "en"],
  );
  assert.equal(isSupportedLocale("zh-CN"), true);
  assert.equal(isSupportedLocale("ja"), true);
  assert.equal(isSupportedLocale("en"), true);
  assert.equal(isSupportedLocale("fr"), false);
});

test("keeps the two translation catalogs in sync", () => {
  assert.deepEqual(Object.keys(MESSAGES.en).sort(), Object.keys(MESSAGES.ja).sort());
  assert.ok(Object.keys(MESSAGES.en).length >= 200);
});

test("translates core lobby and match actions", () => {
  assert.equal(translateText("设置", "en"), "Settings");
  assert.equal(translateText("设置", "ja"), "設定");
  assert.equal(translateText("结束回合", "en"), "End turn");
  assert.equal(translateText("结束回合", "ja"), "ターン終了");
  assert.equal(translateText("设置", "zh-CN"), "设置");
});

test("preserves whitespace and translates dynamic labels", () => {
  assert.equal(translateText("  设置\n", "en"), "  Settings\n");
  assert.equal(translateText("第 2 次投掷", "ja"), "2回目");
  assert.equal(translateText("共 50 张", "en"), "50 cards");
  assert.equal(translateText("以 Luffy 继续", "en"), "Continue as Luffy");
});

test("leaves unknown content unchanged", () => {
  assert.equal(translateText("OP01-001", "en"), "OP01-001");
  assert.equal(translateText("玩家自定义内容", "ja"), "玩家自定义内容");
});
