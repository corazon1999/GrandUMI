import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");
const [exporter, panel] = await Promise.all([
  readSource("../src/lib/deckImageExport.ts"),
  readSource("../src/components/deck-editor/DeckInfoPanel.tsx"),
]);

test("一图流按五列生成主卡组与领袖分区", () => {
  assert.match(exporter, /DECK_IMAGE_COLUMNS = 5/);
  assert.match(exporter, /"MAIN DECK", "主卡组"/);
  assert.match(exporter, /"LEADER", "领袖"/);
  assert.match(exporter, /entry\.count/);
  assert.match(exporter, /card\.number/);
});

test("一图流等待卡图加载并以 PNG 下载", () => {
  assert.match(exporter, /displaySrc\(rawSprite\)/);
  assert.match(exporter, /Promise\.all\(entries\.map/);
  assert.match(exporter, /canvas\.toBlob/);
  assert.match(exporter, /"image\/png"/);
  assert.match(exporter, /anchor\.download = safeFilename/);
});

test("卡组编辑器提供有状态的一图流导出入口", () => {
  assert.match(panel, /downloadDeckImage/);
  assert.match(panel, /"▦ 导出一图流"/);
  assert.match(panel, /正在生成一图流/);
  assert.match(panel, /!leader \|\| entries\.length === 0/);
});
