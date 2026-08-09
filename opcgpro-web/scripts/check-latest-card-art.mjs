import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const DERIVED_ASSET_DIRS = ["cards-thumb", "cards-webp"];

export function expectedLatestArtworkFiles(manifest, publicDir) {
  const expected = [];

  for (const [cardNumber, sprites] of Object.entries(manifest)) {
    if (!Array.isArray(sprites) || sprites.length < 2) continue;

    const latestSprite = sprites.at(-1);
    if (typeof latestSprite !== "string" || !latestSprite.startsWith("/cards/")) continue;

    const relativeWebpPath = latestSprite
      .slice("/cards/".length)
      .replace(/\.(png|jpe?g)$/i, ".webp");

    for (const assetDir of DERIVED_ASSET_DIRS) {
      expected.push({
        cardNumber,
        assetDir,
        filePath: path.join(publicDir, assetDir, relativeWebpPath),
      });
    }
  }

  return expected;
}

export function findMissingLatestArtwork(manifest, publicDir, fileExists = existsSync) {
  return expectedLatestArtworkFiles(manifest, publicDir).filter(
    ({ filePath }) => !fileExists(filePath),
  );
}

function main() {
  const publicDir = path.resolve(process.argv[2] ?? "public");
  const manifestPath = path.join(publicDir, "data", "imageManifest.json");
  const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
  const missing = findMissingLatestArtwork(manifest, publicDir);

  if (missing.length > 0) {
    console.error(`最新异画资源校验失败：缺少 ${missing.length} 个派生文件。`);
    for (const item of missing.slice(0, 20)) {
      console.error(`- ${item.cardNumber} [${item.assetDir}] ${item.filePath}`);
    }
    if (missing.length > 20) console.error(`- 其余 ${missing.length - 20} 个文件已省略`);
    process.exitCode = 1;
    return;
  }

  const cardCount = expectedLatestArtworkFiles(manifest, publicDir).length / DERIVED_ASSET_DIRS.length;
  console.log(`最新异画资源校验通过：${cardCount} 张卡的缩略图和展示图均已就绪。`);
}

const isMain = process.argv[1]
  && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);

if (isMain) main();
