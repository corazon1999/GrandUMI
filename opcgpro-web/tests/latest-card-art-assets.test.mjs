import assert from "node:assert/strict";
import test from "node:test";
import path from "node:path";
import {
  expectedLatestArtworkFiles,
  findMissingLatestArtwork,
} from "../scripts/check-latest-card-art.mjs";

const manifest = {
  "OP16-001": [
    "/cards/op16/OP16-001.png",
    "/cards/op16/OP16-001_01.png",
  ],
  "OP16-002": ["/cards/op16/OP16-002.png"],
  "P-001": ["https://example.com/P-001.png", "https://example.com/P-001-alt.png"],
};

test("只要求多画卡牌的最新本地异画派生资源", () => {
  const publicDir = path.resolve("fixture-public");
  const expected = expectedLatestArtworkFiles(manifest, publicDir);

  assert.deepEqual(
    expected.map(({ cardNumber, assetDir, filePath }) => ({
      cardNumber,
      assetDir,
      relativePath: path.relative(publicDir, filePath).replaceAll("\\", "/"),
    })),
    [
      {
        cardNumber: "OP16-001",
        assetDir: "cards-thumb",
        relativePath: "cards-thumb/op16/OP16-001_01.webp",
      },
      {
        cardNumber: "OP16-001",
        assetDir: "cards-webp",
        relativePath: "cards-webp/op16/OP16-001_01.webp",
      },
    ],
  );
});

test("任一最新异画缩略图或展示图缺失时校验失败", () => {
  const publicDir = path.resolve("fixture-public");
  const available = new Set([
    path.join(publicDir, "cards-thumb", "op16", "OP16-001_01.webp"),
  ]);

  const missing = findMissingLatestArtwork(
    manifest,
    publicDir,
    (filePath) => available.has(filePath),
  );

  assert.equal(missing.length, 1);
  assert.equal(missing[0].cardNumber, "OP16-001");
  assert.equal(missing[0].assetDir, "cards-webp");
});
