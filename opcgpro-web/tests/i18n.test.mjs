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
  assert.ok(Object.keys(MESSAGES.en).length >= 700);
});

test("translates core lobby and match actions", () => {
  assert.equal(translateText("设置", "en"), "Settings");
  assert.equal(translateText("设置", "ja"), "設定");
  assert.equal(translateText("结束回合", "en"), "End turn");
  assert.equal(translateText("结束回合", "ja"), "ターン終了");
  assert.equal(
    translateText("老板来了，等我一会", "en"),
    "My boss is here. Give me a moment.",
  );
  assert.equal(
    translateText("老板来了，等我一会", "ja"),
    "上司が来たので、少し待ってください。",
  );
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

test("translates recently added account and community features", () => {
  assert.equal(translateText("设置密码并登录", "en"), "Set password and sign in");
  assert.equal(translateText("设置密码并登录", "ja"), "パスワードを設定してログイン");
  assert.equal(translateText("卡组广场", "en"), "Deck plaza");
  assert.equal(translateText("卡背广场", "ja"), "カード裏面ギャラリー");
  assert.equal(translateText("▦ 导出一图流", "en"), "▦ Export deck image");
  assert.equal(translateText("效果发动确认", "ja"), "効果発動の確認");
  assert.equal(translateText("Leader 胜率榜", "en"), "Leader win-rate ranking");
});

test("translates new dynamic confirmations, counts, and notifications", () => {
  assert.equal(
    translateText("确定删除卡背“海浪”吗？删除后无法恢复。", "en"),
    "Delete card back “海浪”? This cannot be undone.",
  );
  assert.equal(
    translateText("确定以管理员身份删除已发布卡背“海浪”（作者：路飞）吗？删除后无法恢复。", "en"),
    "Delete published card back “海浪” by 路飞 as an administrator? This cannot be undone.",
  );
  assert.equal(
    translateText("确定删除卡组投稿“红路飞”吗？本地卡组不会被删除。", "ja"),
    "デッキ投稿「红路飞」を削除しますか？ローカルデッキは削除されません。",
  );
  assert.equal(translateText("已压缩至 128KB", "en"), "Compressed to 128 KB");
  assert.equal(translateText("好友 · 3 条新申请", "ja"), "フレンド · 新着申請3件");
  assert.equal(translateText("· 5 条未读", "en"), "· 5 unread");
});
