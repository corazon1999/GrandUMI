import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("手机触摸不会触发桌面悬停预览", async () => {
  const source = await readSource("../src/components/deck-editor/SearchResultPanel.tsx");

  assert.match(source, /onPointerEnter=\{\(e\) => \{/);
  assert.match(source, /if \(e\.pointerType === "mouse"\)/);
  assert.doesNotMatch(source, /onMouseEnter=\{\(e\) => onMouseEnter/);
});

test("手机长按打开详情且不会误添加卡牌", async () => {
  const source = await readSource("../src/components/deck-editor/SearchResultPanel.tsx");

  assert.match(source, /const TOUCH_LONG_PRESS_DELAY = 500/);
  assert.match(source, /suppressClickUntil\.current = Date\.now\(\) \+ TOUCH_CLICK_SUPPRESS_DURATION;\s+onLongPress\(\);/);
  assert.match(source, /if \(Date\.now\(\) < suppressClickUntil\.current\) \{\s+e\.preventDefault\(\);\s+e\.stopPropagation\(\);/);
  assert.match(source, /Math\.hypot\(deltaX, deltaY\) > TOUCH_MOVE_TOLERANCE/);
});

test("组牌页手机详情使用紧凑卡图且保留桌面尺寸", async () => {
  const [searchResult, cardInfo] = await Promise.all([
    readSource("../src/components/deck-editor/SearchResultPanel.tsx"),
    readSource("../src/components/game/CardInfoPanel.tsx"),
  ]);

  assert.match(searchResult, /<CardInfoPanel[\s\S]*?compactMobile\s*\/>/);
  assert.match(cardInfo, /w-\[min\(62vw,14rem\)\] sm:w-full sm:max-w-\[22rem\]/);
  assert.match(cardInfo, /: "w-full max-w-\[22rem\]"/);
});

test("竖屏手机选定领航后退出领航模式", async () => {
  const source = await readSource("../src/components/deck-editor/SearchResultPanel.tsx");

  assert.match(source, /setLeader\(card\);[\s\S]*?window\.matchMedia\("\(max-width: 767px\) and \(orientation: portrait\)"\)\.matches[\s\S]*?setFilterType\(""\);/);
});

test("卡组条目手机轻点只减卡，长按才打开详情", async () => {
  const source = await readSource("../src/components/deck-editor/DeckInfoPanel.tsx");

  assert.match(source, /const TOUCH_LONG_PRESS_DELAY = 500/);
  assert.match(source, /if \(e\.pointerType === "mouse"\) \{\s+onMouseEnter\(card/);
  assert.match(source, /suppressClickUntil\.current = Date\.now\(\) \+ TOUCH_CLICK_SUPPRESS_DURATION;\s+onLongPress\(card\);/);
  assert.match(source, /onClick=\{handleClick\}/);
  assert.match(source, /<CardInfoPanel card=\{modal\} onClose=\{\(\) => setModal\(null\)\} compactMobile \/>/);
});

test("卡组列表使用领航在首位的五列卡面网格", async () => {
  const source = await readSource("../src/components/deck-editor/DeckInfoPanel.tsx");

  const grid = source.match(/className="([^"]+)" data-deck-card-grid/);
  assert.ok(grid, "应标记卡组卡面网格");
  assert.match(grid[1], /\bgrid\b/);
  assert.match(grid[1], /\bgrid-cols-5\b/);
  assert.match(source, /badge="领航"[\s\S]*?entries\.map\(\(entry\)/, "领航卡应排在主卡之前");
  assert.match(source, /badge=\{`× \$\{entry\.count\}`\}/, "数量应覆盖显示在卡面底部");
  assert.match(source, /data-deck-card-grid-item/);
  assert.match(source, /aspect-\[5\/7\]/);
});
