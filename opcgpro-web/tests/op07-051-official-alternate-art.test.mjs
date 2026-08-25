import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";
import { readFile } from "node:fs/promises";
import path from "node:path";
import sharp from "sharp";
import test from "node:test";
import { fileURLToPath } from "node:url";

const testsDir = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(testsDir, "..");
const manifest = JSON.parse(
  readFileSync(path.join(webRoot, "public", "data", "imageManifest.json"), "utf8"),
);

const expectedSprites = [
  "/cards/op07/OP07-051.png",
  "/cards/op07/OP07-051_01.png",
  "/cards/op07/OP07-051_02.png",
  "/cards/op07/OP07-051_03.png",
  "/cards/op07/OP07-051_D.jpg",
  "/cards/op07/OP07-051P_D.jpg",
];

const officialAlternateArt = [
  {
    sprite: "/cards/op07/OP07-051_01.png",
    sha256: "95b847f3340541212cfedc3fc07699929b671c0b513dad2237d1b2e32d8d5f3b",
  },
  {
    sprite: "/cards/op07/OP07-051_02.png",
    sha256: "7338eb99fd2f15d781c7f05253c1ff746cd32eca5e648e60a2ff43b30d60036a",
  },
];

function pngDimensions(buffer) {
  assert.deepEqual([...buffer.subarray(0, 8)], [137, 80, 78, 71, 13, 10, 26, 10]);
  return {
    width: buffer.readUInt32BE(16),
    height: buffer.readUInt32BE(20),
  };
}

test("OP07-051 清单包含简中官网公布的全部六张卡图", () => {
  assert.deepEqual(manifest["OP07-051"], expectedSprites);
  assert.equal(new Set(manifest["OP07-051"]).size, 6);
});

test("OP07-051 两张补充包异画为完整且互不重复的官方 PNG", async () => {
  const hashes = [];
  for (const { sprite, sha256 } of officialAlternateArt) {
    const file = await readFile(path.join(webRoot, "public", sprite));
    assert.deepEqual(pngDimensions(file), { width: 454, height: 635 });
    const actualHash = createHash("sha256").update(file).digest("hex");
    assert.equal(actualHash, sha256, `${sprite} 内容与核验过的简中官网资源不一致`);
    hashes.push(actualHash);
  }
  assert.equal(new Set(hashes).size, officialAlternateArt.length);
});

test("OP07-051 两张补充包异画已生成小图与高清 WebP", async () => {
  for (const { sprite } of officialAlternateArt) {
    const relativeWebpPath = sprite
      .slice("/cards/".length)
      .replace(/\.png$/i, ".webp");
    const thumbMetadata = await sharp(
      path.join(webRoot, "public", "cards-thumb", relativeWebpPath),
    ).metadata();
    const displayMetadata = await sharp(
      path.join(webRoot, "public", "cards-webp", relativeWebpPath),
    ).metadata();

    assert.deepEqual(
      { format: thumbMetadata.format, width: thumbMetadata.width, height: thumbMetadata.height },
      { format: "webp", width: 128, height: 179 },
    );
    assert.deepEqual(
      {
        format: displayMetadata.format,
        width: displayMetadata.width,
        height: displayMetadata.height,
      },
      { format: "webp", width: 454, height: 635 },
    );
  }
});

test("卡牌单包同步携带 OP07-051 的完整异画清单", async () => {
  const bundle = JSON.parse(
    await readFile(path.join(webRoot, "public", "data", "allCards.json"), "utf8"),
  );
  assert.deepEqual(bundle.manifest["OP07-051"], expectedSprites);
});
