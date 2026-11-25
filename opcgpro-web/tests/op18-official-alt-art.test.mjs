import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const webRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const manifest = JSON.parse(readFileSync(path.join(webRoot, "public/data/imageManifest.json"), "utf8"));
const bundle = JSON.parse(readFileSync(path.join(webRoot, "public/data/allCards.json"), "utf8"));

// 2026-11-25 简中官网 OP18 商品清单：119 张正画、33 张本系列异画、7 张旧卡号特别异画。
const variants = [
  "001_01", "004_01", "011_01", "021_01", "022_01", "029_P1", "031_01", "031_02",
  "034_01", "041_01", "046_01", "048_01", "051_01", "060_01", "060_02", "061_01",
  "065_01", "065_02", "079_01", "083_01", "084_01", "084_02", "088_01", "092_01",
  "093_01", "093_02", "096_01", "099_01", "100_01", "113_01", "118_01", "119_01", "119_02",
].map(suffix => `OP18-${suffix}`);
const reprints = [
  "OP13-012_02", "OP13-113_02", "OP17-040_01", "OP17-119_02", "OP17-119_03",
  "ST24-004_01", "ST34-004_01",
];

function assertPublishedSprite(number) {
  const canonical = number.split("_")[0];
  const source = `/cards/${canonical.split("-")[0].toLowerCase()}/${number}.png`;
  const matches = manifest[canonical].filter(sprite => sprite.split("?")[0] === source);
  assert.equal(matches.length, 1, `${number} 必须唯一映射到同一卡号`);
  assert.match(matches[0], /\?v=[a-f0-9]{12}$/, `${number} 必须带内容版本`);
  assert.ok(bundle.manifest[canonical].some(sprite => sprite.split("?")[0] === source), `${number} 必须进入客户端聚合卡表`);
}

test("OP18 的119张基础图和33张中文版异画完整进入发布清单", () => {
  const entries = Object.entries(manifest).filter(([number]) => number.startsWith("OP18-"));
  assert.equal(entries.length, 119);
  assert.equal(entries.flatMap(([, sprites]) => sprites).length, 152);
  for (let index = 1; index <= 119; index++) {
    const number = `OP18-${String(index).padStart(3, "0")}`;
    assert.ok(manifest[number][0].startsWith(`/cards/op18/${number}.png?v=`));
  }
  variants.forEach(assertPublishedSprite);
});

test("OP18商品的7张旧卡号特别异画保留原卡身份及已有画面", () => {
  reprints.forEach(assertPublishedSprite);
  for (const number of new Set(reprints.map(value => value.split("_")[0]))) {
    const set = number.split("-")[0].toLowerCase();
    assert.equal(manifest[number][0].split("?")[0], `/cards/${set}/${number}.png`);
  }
});
