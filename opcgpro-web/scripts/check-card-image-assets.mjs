import sharp from "sharp";
import { readFile, readdir, stat } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const SOURCE_IMAGE_RE = /\.(png|jpe?g)$/i;

export const CARD_IMAGE_VARIANTS = [
  { directory: "cards-thumb", maxWidth: 128 },
  { directory: "cards-webp", maxWidth: 960 },
];

export function derivedRelativePath(sourceRelativePath) {
  return sourceRelativePath.split(/[?#]/, 1)[0].replace(SOURCE_IMAGE_RE, ".webp");
}

async function* walk(dir) {
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const filePath = path.join(dir, entry.name);
    if (entry.isDirectory()) yield* walk(filePath);
    else if (SOURCE_IMAGE_RE.test(entry.name)) yield filePath;
  }
}

async function inspectDerivedFile(filePath, sourceMtimeMs, maxWidth) {
  let fileStat;
  try {
    fileStat = await stat(filePath);
  } catch {
    return "缺失";
  }

  if (fileStat.size <= 0) return "空文件";
  if (fileStat.mtimeMs < sourceMtimeMs) return "早于原图";

  try {
    const metadata = await sharp(filePath).metadata();
    if (metadata.format !== "webp") return `格式为 ${metadata.format ?? "未知"}`;
    if (!metadata.width || !metadata.height) return "无法读取尺寸";
    if (metadata.width > maxWidth) return `宽度 ${metadata.width}px 超过 ${maxWidth}px`;
  } catch (error) {
    return `无法解码：${error.message}`;
  }

  return null;
}

export async function auditCardImageAssets(publicDir, concurrency = 16) {
  const sourceDir = path.join(publicDir, "cards");
  const sources = [];
  for await (const sourcePath of walk(sourceDir)) {
    sources.push({
      sourcePath,
      relativePath: path.relative(sourceDir, sourcePath),
      sourceStat: await stat(sourcePath),
    });
  }

  const expectedPaths = new Map();
  const duplicateOutputs = [];
  for (const source of sources) {
    const outputRelativePath = derivedRelativePath(source.relativePath).toLowerCase();
    const previous = expectedPaths.get(outputRelativePath);
    if (previous) duplicateOutputs.push([previous, source.relativePath]);
    else expectedPaths.set(outputRelativePath, source.relativePath);
  }

  const tasksByPath = new Map();
  for (const source of sources) {
    for (const variant of CARD_IMAGE_VARIANTS) {
      const outputRelativePath = derivedRelativePath(source.relativePath);
      tasksByPath.set(`${variant.directory}/${outputRelativePath.toLowerCase()}`, {
        variant,
        outputRelativePath,
        sourceMtimeMs: source.sourceStat.mtimeMs,
      });
    }
  }

  const manifestPath = path.join(publicDir, "data", "imageManifest.json");
  const manifest = JSON.parse(await readFile(manifestPath, "utf8"));
  const manifestReferences = new Set();
  for (const sprites of Object.values(manifest)) {
    if (!Array.isArray(sprites)) continue;
    for (const sprite of sprites) {
      if (typeof sprite !== "string" || !sprite.startsWith("/cards/")) continue;
      const outputRelativePath = derivedRelativePath(sprite.slice("/cards/".length));
      manifestReferences.add(outputRelativePath.toLowerCase());
      for (const variant of CARD_IMAGE_VARIANTS) {
        const key = `${variant.directory}/${outputRelativePath.toLowerCase()}`;
        if (!tasksByPath.has(key)) {
          tasksByPath.set(key, { variant, outputRelativePath, sourceMtimeMs: 0 });
        }
      }
    }
  }

  const failures = [];
  let nextIndex = 0;
  const tasks = [...tasksByPath.values()];

  async function worker() {
    while (nextIndex < tasks.length) {
      const task = tasks[nextIndex++];
      const outputPath = path.join(publicDir, task.variant.directory, task.outputRelativePath);
      const reason = await inspectDerivedFile(
        outputPath,
        task.sourceMtimeMs,
        task.variant.maxWidth,
      );
      if (reason) {
        failures.push({
          variant: task.variant.directory,
          relativePath: task.outputRelativePath,
          reason,
        });
      }
    }
  }

  await Promise.all(Array.from({ length: concurrency }, worker));
  return {
    sourceCount: sources.length,
    referencedCount: manifestReferences.size,
    failures,
    duplicateOutputs,
  };
}

function printLimited(items, formatter) {
  for (const item of items.slice(0, 50)) console.error(`- ${formatter(item)}`);
  if (items.length > 50) console.error(`- 其余 ${items.length - 50} 项已省略`);
}

async function main() {
  const publicDir = path.resolve(process.argv[2] ?? "public");
  const result = await auditCardImageAssets(publicDir);

  if (result.duplicateOutputs.length > 0) {
    console.error(`卡图资源校验失败：${result.duplicateOutputs.length} 组原图会生成同名 WebP。`);
    printLimited(result.duplicateOutputs, ([first, second]) => `${first} / ${second}`);
  }

  if (result.failures.length > 0) {
    console.error(`卡图资源校验失败：发现 ${result.failures.length} 个缺失、过期或损坏的派生文件。`);
    printLimited(
      result.failures,
      (item) => `[${item.variant}] ${item.relativePath}（${item.reason}）`,
    );
  }

  if (result.duplicateOutputs.length > 0 || result.failures.length > 0) {
    process.exitCode = 1;
    return;
  }

  console.log(
    `卡图资源校验通过：${result.sourceCount} 张原图及清单引用的 ${result.referencedCount} 张卡图，缩略图和高清展示图均完整、最新且可正常解码。`,
  );
}

const isMain = process.argv[1]
  && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);

if (isMain) await main();
