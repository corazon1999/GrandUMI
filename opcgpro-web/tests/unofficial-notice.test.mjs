import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("登录页展示学术研究项目介绍和免责声明", async () => {
  const source = await readSource("../src/components/home/LoginPanel.tsx");
  const loginPanelEnd = source.indexOf("</motion.div>");
  const noticeStart = source.indexOf('aria-label="项目免责声明"');

  assert.match(source, /TCG Intelligence Project｜集换式卡牌博弈智能ai研究项目。/);
  assert.match(source, /本项目为独立学术研究项目，与任何卡牌游戏的发行商、版权方或商标权利人均无隶属、授权或合作关系。/);
  assert.match(source, /相关商标、卡牌名称及美术素材归各自权利人所有。/);
  assert.match(source, /本平台不提供付费服务。/);
  assert.ok(loginPanelEnd >= 0 && noticeStart > loginPanelEnd, "免责声明应位于登录卡片下方");
});

test("登录页说明区域兼容手机竖屏和安全区", async () => {
  const source = await readSource("../src/components/home/LoginPanel.tsx");

  assert.match(source, /overflow-y-auto/);
  assert.match(source, /env\(safe-area-inset-top\)/);
  assert.match(source, /env\(safe-area-inset-bottom\)/);
  assert.match(source, /mb-auto mt-5 w-full max-w-5xl/);
  assert.match(source, /text-xs leading-5 text-gray-500 sm:text-sm sm:leading-6/);
});
