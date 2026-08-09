import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = await readFile(
  new URL("../src/components/home/MainPanel.tsx", import.meta.url),
  "utf8",
);

test("桌面侧栏使用紧凑图标导航并保留当前页状态", () => {
  assert.match(source, /aria-label="桌面主要导航"[^>]*w-20/);
  assert.match(source, /function SidebarButton/);
  assert.match(source, /aria-current=\{active \? "page" : undefined\}/);

  for (const label of ["大厅", "卡组", "卡牌图鉴", "Leader榜", "卡背广场", "我的", "对局记录"]) {
    assert.match(source, new RegExp(`SidebarButton label="${label}"`));
  }
});

test("侧栏图标在悬停和键盘聚焦时显示文字提示", () => {
  assert.match(source, /role="tooltip"/);
  assert.match(source, /group-hover:opacity-100/);
  assert.match(source, /group-focus:opacity-100/);
  assert.match(source, /aria-label=\{label\}/);
});
