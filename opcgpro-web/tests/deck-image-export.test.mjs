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

test("一图流等待卡图加载并生成可预览的 PNG", () => {
  assert.match(exporter, /displaySrc\(rawSprite\)/);
  assert.match(exporter, /Promise\.all\(entries\.map/);
  assert.match(exporter, /canvas\.toBlob/);
  assert.match(exporter, /"image\/png"/);
  assert.match(exporter, /export async function generateDeckImage/);
  assert.match(exporter, /filename: safeFilename/);
});

test("卡组编辑器先显示一图流弹窗，再由玩家主动下载", () => {
  assert.match(panel, /generateDeckImage/);
  assert.match(panel, /"▦ 导出一图流"/);
  assert.match(panel, /正在生成预览/);
  assert.match(panel, /data-testid="deck-image-preview"/);
  assert.match(panel, /预览不会自动下载/);
  assert.match(panel, />\s*下载 PNG\s*</);
  assert.match(panel, /downloadGeneratedDeckImage\(imagePreview\.url, imagePreview\.filename\)/);
  assert.match(panel, /!leader \|\| entries\.length === 0/);
});
