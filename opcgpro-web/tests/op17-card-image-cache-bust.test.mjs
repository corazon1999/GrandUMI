import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const testsDir = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(testsDir, "..");
const manifest = JSON.parse(
  readFileSync(path.join(webRoot, "public", "data", "imageManifest.json"), "utf8"),
);

test("OP17 使用官网最新正画、异画与独立内容版本键", () => {
  const op17Entries = Object.entries(manifest).filter(([number]) =>
    number.startsWith("OP17-"),
  );
  const op17Sprites = op17Entries.flatMap(([, sprites]) => sprites);

  assert.equal(op17Entries.length, 119);
  assert.equal(op17Sprites.length, 158);
  assert.equal(
    op17Entries.reduce((count, [, sprites]) => count + sprites.length - 1, 0),
    39,
  );
  assert.ok(
    op17Sprites.every((sprite) =>
      /^\/cards\/op17\/OP17-\d{3}(?:_\d{2})?\.png\?v=[a-f0-9]{12}$/.test(sprite),
    ),
  );
  assert.ok(op17Sprites.every((sprite) => !sprite.includes("_v2")));
  assert.ok(op17Sprites.every((sprite) => !sprite.includes(".jpg")));
});

test("OP17 商品的 11 张旧卡号特别异画已进入对应卡牌清单", () => {
  const expectedReprints = [
    ["P-084", "/cards/p/P-084_01.png"],
    ["P-107", "/cards/p/P-107_02.png"],
    ["EB04-007", "/cards/eb04/EB04-007_02.png"],
    ["EB04-061", "/cards/eb04/EB04-061_03.png"],
    ["OP12-056", "/cards/op12/OP12-056_02.png"],
    ["OP13-028", "/cards/op13/OP13-028_02.png"],
    ["OP14-108", "/cards/op14/OP14-108_01.png"],
    ["OP16-098", "/cards/op16/OP16-098_02.png"],
    ["ST27-005", "/cards/st27/ST27-005_01.png"],
    ["ST31-004", "/cards/st31/ST31-004_01.png"],
    ["ST32-002", "/cards/st32/ST32-002_01.png"],
  ];

  for (const [number, spritePrefix] of expectedReprints) {
    assert.ok(
      manifest[number]?.some((sprite) => sprite.startsWith(`${spritePrefix}?v=`)),
      `${number} 缺少 OP17 特别异画`,
    );
  }
});
