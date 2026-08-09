import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("卡组编辑器工具栏为全局设置按钮预留右侧空间", async () => {
  const source = await readSource("../src/components/deck-editor/DeckInfoPanel.tsx");
  const heading = source.match(/data-deck-toolbar-heading[\s\S]*?className="([^"]+)"/);
  const actions = source.match(/data-deck-toolbar-actions[\s\S]*?className="([^"]+)"/);

  assert.ok(heading, "应标记卡组工具栏标题区");
  assert.ok(actions, "应标记卡组工具栏操作区");
  assert.match(heading[1], /\bpr-16\b/);
  assert.match(actions[1], /\bpr-16\b/);
});

test("卡组操作按钮使用独立五列布局", async () => {
  const source = await readSource("../src/components/deck-editor/DeckInfoPanel.tsx");
  const actions = source.match(/data-deck-toolbar-actions[\s\S]*?className="([^"]+)"/);

  assert.ok(actions);
  assert.match(actions[1], /\bgrid\b/);
  assert.match(actions[1], /\bgrid-cols-5\b/);
  for (const label of ["新建", "读取", "清空", "导出", "导入"]) {
    assert.match(source, new RegExp(`>\\s*${label}\\s*<\\/button>`));
  }
});
