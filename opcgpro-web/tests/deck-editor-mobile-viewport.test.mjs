import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("卡组编辑器使用动态视口高度", async () => {
  const source = await readSource("../src/app/deck-editor/page.tsx");
  const dynamicViewportContainers = source.match(/h-\[100dvh\]/g) ?? [];

  assert.equal(dynamicViewportContainers.length, 3, "加载、错误和编辑状态均应跟随移动端动态视口");
});

test("卡组操作面板避开手机底部安全区", async () => {
  const source = await readSource("../src/app/deck-editor/page.tsx");
  const panel = source.match(/data-deck-editor-panel[\s\S]*?className=\{`([^`]+)`\}/);

  assert.ok(panel, "应标记卡组操作面板");
  assert.match(panel[1], /pb-\[env\(safe-area-inset-bottom\)\]/);
});

test("手机端将牌池、卡组与筛选拆分为独立视图", async () => {
  const source = await readSource("../src/app/deck-editor/page.tsx");

  assert.match(source, /data-deck-mobile-nav/);
  assert.match(source, />\s*牌池\s*<\/button>/);
  assert.match(source, />\s*卡组\s*<\/button>/);
  assert.match(source, /data-deck-search-panel/);
  assert.match(source, /data-deck-card-pool/);
  assert.match(source, /mobileFiltersOpen \? "absolute inset-x-0 bottom-0 top-12 z-40 flex" : "hidden"/);
});

test("桌面端仍保持筛选、牌池、卡组三栏布局", async () => {
  const source = await readSource("../src/app/deck-editor/page.tsx");
  const searchPanel = source.match(/data-deck-search-panel[\s\S]*?className=\{`([^`]+)`\}/);
  const cardPool = source.match(/data-deck-card-pool[\s\S]*?className=\{`([^`]+)`\}/);
  const deckPanel = source.match(/data-deck-editor-panel[\s\S]*?className=\{`([^`]+)`\}/);

  assert.ok(searchPanel);
  assert.ok(cardPool);
  assert.ok(deckPanel);
  assert.match(searchPanel[1], /md:static md:flex md:w-48/);
  assert.match(cardPool[1], /md:block/);
  assert.match(deckPanel[1], /md:block md:w-80/);
});
