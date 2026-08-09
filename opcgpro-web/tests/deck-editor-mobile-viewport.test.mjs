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
  const panel = source.match(/data-deck-editor-panel[\s\S]*?className="([^"]+)"/);

  assert.ok(panel, "应标记卡组操作面板");
  assert.ok(panel[1].split(/\s+/).includes("pb-[env(safe-area-inset-bottom)]"));
});
