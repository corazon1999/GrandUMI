import { readFile, stat } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

export const CARD_IMAGE_ASSET_DIRECTORIES = ["cards-thumb", "cards-webp"];

const CARD_SOURCE_RE = /^\/cards\/(.+)\.(png|jpe?g)$/i;

/**
 * 把图片清单中的原图地址转换成正式服必须具备的 WebP 相对路径。
 * 查询参数只用于缓存击穿，不属于磁盘文件名。
 */
export function manifestSpriteToWebpPath(sprite) {
  if (typeof sprite !== "string") return null;
  const pathname = sprite.split(/[?#]/, 1)[0];
  const match = pathname.match(CARD_SOURCE_RE);
  return match ? `${match[1]}.webp` : null;
}

/** 返回清单引用的全部缩略图和高清图路径，并自动去重。 */
export function expectedManifestAssetPaths(manifest) {
  const paths = new Set();
  for (const sprites of Object.values(manifest)) {
    if (!Array.isArray(sprites)) continue;
    for (const sprite of sprites) {
      const relativeWebpPath = manifestSpriteToWebpPath(sprite);
      if (!relativeWebpPath) continue;
      for (const directory of CARD_IMAGE_ASSET_DIRECTORIES) {
        paths.add(path.posix.join(directory, relativeWebpPath.replaceAll("\\", "/")));
      }
    }
  }
  return [...paths].sort();
}

export async function findMissingManifestAssets(manifest, assetRoot, concurrency = 32) {
  const expected = expectedManifestAssetPaths(manifest);
  const missing = [];
  let nextIndex = 0;

  async function worker() {
    while (nextIndex < expected.length) {
      const relativePath = expected[nextIndex++];
      try {
        const fileStat = await stat(path.join(assetRoot, ...relativePath.split("/")));
        if (!fileStat.isFile() || fileStat.size <= 0) missing.push(relativePath);
      } catch {
        missing.push(relativePath);
      }
    }
  }

  await Promise.all(Array.from({ length: Math.max(1, concurrency) }, worker));
  return { expected, missing: missing.sort() };
}

async function main() {
  const args = process.argv.slice(2);
  const listOnly = args.includes("--list");
  const positional = args.filter((arg) => arg !== "--list");
  if (positional.length !== 2) {
    console.error("用法：node check-card-image-manifest.mjs <imageManifest.json> <资源根目录> [--list]");
    process.exitCode = 2;
    return;
  }

  const [manifestPath, assetRoot] = positional.map((value) => path.resolve(value));
  const manifest = JSON.parse(await readFile(manifestPath, "utf8"));
  const expected = expectedManifestAssetPaths(manifest);

  if (listOnly) {
    process.stdout.write(`${expected.join("\n")}\n`);
    return;
  }

  const result = await findMissingManifestAssets(manifest, assetRoot);
  if (result.missing.length > 0) {
    console.error(`卡图清单完整性校验失败：${result.missing.length} 个派生文件缺失或为空。`);
    for (const relativePath of result.missing.slice(0, 50)) {
      console.error(`- ${relativePath}`);
    }
    if (result.missing.length > 50) {
      console.error(`- 其余 ${result.missing.length - 50} 项已省略`);
    }
    process.exitCode = 1;
    return;
  }

  console.log(`卡图清单完整性校验通过：${result.expected.length} 个缩略图和高清图文件均已就绪。`);
}

const isMain = process.argv[1]
  && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);

if (isMain) await main();
