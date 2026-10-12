import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

const source = await readFile(new URL("../src/lib/sprite.ts", import.meta.url), "utf8");
function fixture(hostname = "ygo.grand-umi.com", assetOrigin = "") {
  const mod = { exports: {} };
  vm.runInNewContext(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText, {
    module: mod, exports: mod.exports, URL,
    require: name => name.endsWith("dataVersion") ? { DATA_VERSION: "版本" }
      : { CARD_IMAGE_BASE_VERSION: "原图版本", CARD_IMAGE_REVISIONS: {} },
    process: { env: { NEXT_PUBLIC_ASSET_ORIGIN: assetOrigin } },
    window: { location: { hostname, href: `https://${hostname}/game`, origin: `https://${hostname}` } },
  });
  return mod.exports;
}

test("派生图失败后优先同名原图 WebP，原 PNG 使用独立缓存键且回退有限", () => {
  const f = fixture();
  const raw = "/cards/op17/OP17-099.png?user=1#card";
  let current = f.displaySrc(raw);
  const seen = new Set([current]);
  for (let step = 0; step < 30; step++) {
    const next = f.nextCardImageSrc(current, raw, "https://example.org/card.jpg", "display");
    if (next === current) break;
    assert.ok(!seen.has(next), `图片回退不应循环：${next}`);
    seen.add(next); current = next;
  }
  const sources = [...seen];
  const webp = sources.findIndex(value => value.startsWith("/cards/op17/OP17-099.webp"));
  const png = sources.findIndex(value => value.includes("fallback=original-v1#card"));
  assert.ok(webp >= 0 && png > webp);
  assert.ok(sources[png].includes("?user=1&fallback="));
  assert.equal(current, "https://direct.grand-umi.com/sprites-thumb/CardBack.webp");
});

test("原生图片回退兼容资源域名，保留原查询参数并避开旧 404 缓存", () => {
  const f = fixture("ygo.grand-umi.com", "https://assets.grand-umi.com");
  const image = { src: "https://assets.grand-umi.com/cards-webp/op17/OP17-099.webp", dataset: {} };
  f.advanceImageFallback(image, ["https://assets.grand-umi.com/cards/op17/OP17-099.png"]);
  assert.match(image.src, /^https:\/\/assets\.grand-umi\.com\/cards\/op17\/OP17-099.webp\?v=/);
});

test("测试环境图片不跨正式线路，外部地址不被替换扩展名", () => {
  const f = fixture("test.grand-umi.com");
  const image = { src: "https://example.org/card.png", dataset: {} };
  f.advanceImageFallback(image, ["https://example.org/card.png"]);
  assert.equal(image.src, "/sprites-thumb/CardBack.webp");
});
