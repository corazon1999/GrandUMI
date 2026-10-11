import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import vm from "node:vm";
import { imageFingerprint, updateImageCacheState, verifyCardImageVersions } from "../scripts/build-card-image-versions.mjs";

const require = createRequire(import.meta.url);
const ts = require("typescript");
const sprite = readFileSync(new URL("../src/lib/sprite.ts", import.meta.url), "utf8");
const manifest = JSON.parse(readFileSync(new URL("../public/data/imageManifest.json", import.meta.url), "utf8"));
const state = JSON.parse(readFileSync(new URL("../scripts/card-image-cache-state.json", import.meta.url), "utf8"));
function compiledSprite(dataVersion, revisions = state.revisions) {
  const exports = {};
  vm.runInNewContext(ts.transpileModule(sprite, { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText, {
    exports, URL, process: { env: { NEXT_PUBLIC_ASSET_ORIGIN: "https://assets.grand-umi.com" } },
    require: id => id.endsWith("/dataVersion") ? { DATA_VERSION: dataVersion }
      : { CARD_IMAGE_BASE_VERSION: state.baseVersion, CARD_IMAGE_REVISIONS: revisions },
  });
  return exports;
}

test("修改卡牌数据版本不会刷新任意卡图地址，并保留当前线上已下载的图片", () => {
  const before = compiledSprite("4dcda4b0421e");
  const after = compiledSprite("卡牌文字更新");
  for (const raw of Object.values(manifest).flat()) {
    for (const variant of ["thumbSrc", "displaySrc"]) {
      assert.equal(after[variant](raw), before[variant](raw));
      assert.ok(before[variant](raw).includes(state.baseVersion));
    }
  }
  assert.equal(before.thumbSrc("/cards/op01/OP01-001.png"), "https://assets.grand-umi.com/cards-thumb/op01/OP01-001.webp?v=4dcda4b0421e-r6");
  assert.equal(before.displaySrc("/cards/st38/ST38-001.png?v=dfb953209398"), "https://assets.grand-umi.com/cards-webp/st38/ST38-001.webp?v=dfb953209398&r=4dcda4b0421e-r6");
});

test("仅修改一张图片时，其余图片继续使用原缓存", () => {
  const previous = { fingerprints: { "op01/OP01-001": "old", "op01/OP01-002": "same" }, revisions: {}, baseVersion: state.baseVersion };
  const next = updateImageCacheState(previous, { "op01/OP01-001": "new", "op01/OP01-002": "same" });
  assert.deepEqual(next.revisions, { "op01/OP01-001": "new" });
  const oldSprite = compiledSprite("v1");
  const newSprite = compiledSprite("v2", next.revisions);
  assert.notEqual(oldSprite.thumbSrc("/cards/op01/OP01-001.png"), newSprite.thumbSrc("/cards/op01/OP01-001.png"));
  assert.equal(oldSprite.thumbSrc("/cards/op01/OP01-002.png"), newSprite.thumbSrc("/cards/op01/OP01-002.png"));
});

test("缩略图或高清图改变都会得到新内容版本，片段不干扰缓存查询参数", () => {
  assert.notEqual(imageFingerprint(Buffer.from("ab"), Buffer.from("c")), imageFingerprint(Buffer.from("a"), Buffer.from("bc")));
  assert.notEqual(imageFingerprint(Buffer.from("a"), Buffer.from("b")), imageFingerprint(Buffer.from("a"), Buffer.from("c")));
  const compiled = compiledSprite("v1");
  assert.equal(compiled.thumbSrc("/cards/op01/OP01-001.png?v=abc#detail"), "https://assets.grand-umi.com/cards-thumb/op01/OP01-001.webp?v=abc&r=4dcda4b0421e-r6#detail");
});

test("所有卡图均有独立版本记录且生成物与记录一致", () => {
  assert.equal(verifyCardImageVersions(), Object.values(manifest).flat().length);
});
