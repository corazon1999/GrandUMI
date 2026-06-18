// 给卡组编辑器实际使用的 public/cards 树生成小尺寸 WebP 缩略图，输出到 public/cards-thumb。
// 网格(64px)与悬停预览(240px)用它，体积约为原 PNG 的 15%。
//
// 用法：npm run gen:card-thumbs
// 增量：输出已存在且不旧于源文件则跳过，可反复运行。
// 注意：public/cards 是指向 D:\Self\GrandUMI\CardImages 的目录联接，sharp 会自动跟随。
//       cards-thumb 为本地派生产物(已 gitignore)，部署时需与 CardImages 一并同步到服务器，
//       或在服务器上跑一次本脚本。

import sharp from "sharp";
import { readdir, mkdir, stat } from "fs/promises";
import path from "path";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const SRC = path.join(__dirname, "..", "public", "cards");
const DST = path.join(__dirname, "..", "public", "cards-thumb");
const WIDTH = 256;
const QUALITY = 72;
const CONC = 8;

async function* walk(dir) {
  for (const e of await readdir(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) yield* walk(p);
    else if (/\.png$/i.test(e.name)) yield p;
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

let done = 0, ok = 0, skip = 0, fail = 0;

async function main() {
  const all = [];
  for await (const f of walk(SRC)) all.push(f);
  console.log(`共 ${all.length} 张待处理 → ${DST} (宽${WIDTH} 质量${QUALITY})`);

  let i = 0;
  async function worker() {
    while (i < all.length) {
      const f = all[i++];
      const rel = path.relative(SRC, f);
      const out = path.join(DST, rel.replace(/\.png$/i, ".webp"));
      try {
        if (await isFresh(f, out)) {
          skip++;
        } else {
          await mkdir(path.dirname(out), { recursive: true });
          await sharp(f)
            .resize({ width: WIDTH, withoutEnlargement: true })
            .webp({ quality: QUALITY })
            .toFile(out);
          ok++;
        }
      } catch (e) {
        fail++;
        console.error("FAIL", rel, e.message);
      }
      if (++done % 300 === 0) console.log(`${done}/${all.length}`);
    }
  }
  await Promise.all(Array.from({ length: CONC }, worker));
  console.log(`完成: 新生成=${ok} 跳过=${skip} 失败=${fail}`);
}

main();
