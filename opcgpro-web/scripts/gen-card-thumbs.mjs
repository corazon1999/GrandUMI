// 给 public/cards 树生成两档 WebP：
// - public/cards-thumb：宽 128，供网格、列表和对战小卡使用。
// - public/cards-webp：最大宽 960，供悬停、详情和全屏大图使用。
// 同时生成实际仅以 20px 展示的状态图标 WebP。
//
// 用法：npm run gen:card-thumbs
// 增量：输出已存在且不旧于源文件则跳过；传入 --force 可强制重建全部缩略图。
// 注意：public/cards 是指向 D:\Self\GrandUMI\CardImages 的目录联接，sharp 会自动跟随。
//       cards-thumb/cards-webp 为本地派生产物(已 gitignore)，部署时需与 CardImages 一并同步到服务器，
//       或在服务器上跑一次本脚本。

import sharp from "sharp";
import { readdir, mkdir, stat } from "fs/promises";
import path from "path";
import { fileURLToPath } from "url";
import { auditCardImageAssets } from "./check-card-image-assets.mjs";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const SRC = path.join(__dirname, "..", "public", "cards");
const THUMB_DST = path.join(__dirname, "..", "public", "cards-thumb");
const DISPLAY_DST = path.join(__dirname, "..", "public", "cards-webp");
const STATUS_ICON_SRC = path.join(__dirname, "..", "public", "status-icons", "cannot-attack.png");
const STATUS_ICON_DST = path.join(__dirname, "..", "public", "status-icons", "cannot-attack.webp");
const THUMB_WIDTH = 128;
const THUMB_QUALITY = 62;
const DISPLAY_WIDTH = 960;
const DISPLAY_QUALITY = 84;
const CONC = 8;
const FORCE = process.argv.includes("--force");

async function* walk(dir) {
  for (const e of await readdir(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) yield* walk(p);
    else if (/\.(png|jpe?g)$/i.test(e.name)) yield p;
  }
}

async function isFresh(src, out) {
  try {
    const [s, o] = await Promise.all([stat(src), stat(out)]);
    return o.mtimeMs >= s.mtimeMs; // 输出不旧于源 → 跳过
  } catch {
    return false;
  }
}

let done = 0, thumbOk = 0, displayOk = 0, skip = 0, fail = 0;

function outputPath(root, relativePath) {
  return path.join(root, relativePath.replace(/\.(png|jpe?g)$/i, ".webp"));
}

async function generateVariant(src, out, width, quality, alphaQuality) {
  if (!FORCE && await isFresh(src, out)) return false;
  await mkdir(path.dirname(out), { recursive: true });
  await sharp(src)
    .resize({ width, withoutEnlargement: true })
    .webp({ quality, alphaQuality, smartSubsample: true })
    .toFile(out);
  return true;
}

async function generateStatusIcon() {
  const changed = await generateVariant(
    STATUS_ICON_SRC,
    STATUS_ICON_DST,
    96,
    80,
    90,
  );
  console.log(`状态图标: ${changed ? "已生成" : "已是最新"} → ${STATUS_ICON_DST}`);
}

async function main() {
  const all = [];
  for await (const f of walk(SRC)) all.push(f);
  console.log(
    `共 ${all.length} 张待处理 → 小图 ${THUMB_WIDTH}px / 展示图最大 ${DISPLAY_WIDTH}px`,
  );

  let i = 0;
  async function worker() {
    while (i < all.length) {
      const f = all[i++];
      const rel = path.relative(SRC, f);
      const thumbOut = outputPath(THUMB_DST, rel);
      const displayOut = outputPath(DISPLAY_DST, rel);
      try {
        const [newThumb, newDisplay] = await Promise.all([
          generateVariant(f, thumbOut, THUMB_WIDTH, THUMB_QUALITY, 80),
          generateVariant(f, displayOut, DISPLAY_WIDTH, DISPLAY_QUALITY, 90),
        ]);
        if (!newThumb && !newDisplay) {
          skip++;
        }
        if (newThumb) thumbOk++;
        if (newDisplay) displayOk++;
      } catch (e) {
        fail++;
        console.error("FAIL", rel, e.message);
      }
      if (++done % 300 === 0) console.log(`${done}/${all.length}`);
    }
  }
  await Promise.all(Array.from({ length: CONC }, worker));
  await generateStatusIcon();
  console.log(
    `完成: 小图新生成=${thumbOk} 展示图新生成=${displayOk} 全部跳过=${skip} 失败=${fail}`,
  );
  if (fail > 0) process.exitCode = 1;

  const audit = await auditCardImageAssets(path.join(__dirname, "..", "public"));
  if (audit.duplicateOutputs.length > 0 || audit.failures.length > 0) {
    console.error(
      `生成后校验失败：同名输出=${audit.duplicateOutputs.length}，缺失/过期/损坏=${audit.failures.length}`,
    );
    for (const item of audit.failures.slice(0, 20)) {
      console.error(`- [${item.variant}] ${item.relativePath}（${item.reason}）`);
    }
    process.exitCode = 1;
  } else {
    console.log(`生成后校验通过：${audit.sourceCount} 张原图的两档 WebP 均完整可用。`);
  }
}

main();
