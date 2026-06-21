/**
 * 将 public/cards 下所有 PNG 转换为 WebP，并更新 imageManifest.json
 * 运行：node scripts/convert-to-webp.mjs
 */
import sharp from 'sharp';
import { readdir, unlink, readFile, writeFile } from 'fs/promises';
import { existsSync } from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const CARDS_DIR = path.join(__dirname, '..', 'public', 'cards');
const MANIFEST_PATH = path.join(__dirname, '..', 'public', 'data', 'imageManifest.json');
const QUALITY = 85;
const CONCURRENCY = 8;

async function collectPngs(dir) {
  const entries = await readdir(dir, { withFileTypes: true });
  const results = [];
  for (const e of entries) {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) results.push(...await collectPngs(full));
    else if (e.name.endsWith('.png')) results.push(full);
  }
  return results;
}

async function convertFile(pngPath) {
  const webpPath = pngPath.replace(/\.png$/, '.webp');
  await sharp(pngPath).webp({ quality: QUALITY }).toFile(webpPath);
  await unlink(pngPath);
}

async function runWithConcurrency(tasks, limit) {
  let i = 0;
  let done = 0;
  const total = tasks.length;
  async function worker() {
    while (i < total) {
      const task = tasks[i++];
      await task();
      done++;
      if (done % 500 === 0) console.log(`  ${done}/${total}`);
    }
  }
  await Promise.all(Array.from({ length: limit }, worker));
}

// 更新 manifest：把路径里的 .png 替换为 .webp
async function updateManifest() {
  if (!existsSync(MANIFEST_PATH)) return;
  const raw = JSON.parse(await readFile(MANIFEST_PATH, 'utf-8'));
  for (const key of Object.keys(raw)) {
    raw[key] = raw[key].map(url => url.replace(/\.png$/, '.webp'));
  }
  await writeFile(MANIFEST_PATH, JSON.stringify(raw, null, 2));
  console.log('imageManifest.json 已更新');
}

const pngs = await collectPngs(CARDS_DIR);
console.log(`找到 ${pngs.length} 张 PNG，开始转换（quality=${QUALITY}）...`);

await runWithConcurrency(pngs.map(p => () => convertFile(p)), CONCURRENCY);
console.log(`转换完成：${pngs.length} 张`);

await updateManifest();
