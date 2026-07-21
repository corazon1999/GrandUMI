#!/usr/bin/env node
/**
 * 按卡集下载卡图到 opcgpro-web/public/cards/{setlower}/{cardNumber}.{ext}
 *
 * 使用：
 *   node tools/download-sprites.mjs OP16
 *   node tools/download-sprites.mjs OP16 --concurrency=5
 *   node tools/download-sprites.mjs OP16 --force   # 强制重下（默认断点续传）
 *
 * 图源：卡牌数据/{SET}.json 中每张卡的 image 字段
 */

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, "..");

const setCode = (process.argv[2] || "OP16").toUpperCase();
const concurrency = parseInt(process.argv.find(a => a.startsWith("--concurrency="))?.split("=")[1] ?? "4", 10);
const force = process.argv.includes("--force");

const srcPath = path.join(ROOT, "卡牌数据", `${setCode}.json`);
const outDir = path.join(ROOT, "opcgpro-web", "public", "cards", setCode.toLowerCase());

if (!fs.existsSync(srcPath)) {
  console.error(`!! 未找到卡集数据: ${srcPath}`);
  process.exit(1);
}
fs.mkdirSync(outDir, { recursive: true });

const cards = JSON.parse(fs.readFileSync(srcPath, "utf8"));
const tasks = [];

for (const card of cards) {
  if (!card.image || !card.number) continue;
  // 后缀沿用 URL 中的扩展名
  const m = card.image.match(/\.(png|jpg|jpeg|webp)(?:\?|$)/i);
  const ext = (m?.[1] ?? "png").toLowerCase();
  // 官网异画的详情 cardNumber 仍是基础卡号，但图片文件名带 `_01` 等后缀。
  // 优先从 URL 取完整卡号，避免异画覆盖正画。
  const escapedNumber = card.number.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const imageNumber = decodeURIComponent(card.image)
    .match(new RegExp(`${escapedNumber}(?:_[A-Za-z0-9-]+)?(?=\\.(?:png|jpg|jpeg|webp)(?:\\?|$))`, "i"))?.[0]
    ?? card.number;
  const filename = `${imageNumber}.${ext}`;
  const savePath = path.join(outDir, filename);
  tasks.push({ url: card.image, savePath, name: filename });
}

console.log(`==> ${setCode} 共 ${tasks.length} 张待下载`);
console.log(`    输出目录: ${outDir}`);
console.log(`    并发: ${concurrency}, 强制: ${force}`);

let ok = 0, skip = 0, fail = 0;
const failed = [];

async function downloadOne(t) {
  if (!force && fs.existsSync(t.savePath)) {
    const st = fs.statSync(t.savePath);
    if (st.size > 1024) { skip++; return; }
  }
  try {
    const res = await fetch(t.url, {
      headers: {
        "Referer": "https://www.onepiece-cardgame.cn/",
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124.0 Safari/537.36",
      },
    });
    if (!res.ok) {
      fail++;
      failed.push(`${t.name}: HTTP ${res.status}`);
      return;
    }
    const buf = Buffer.from(await res.arrayBuffer());
    fs.writeFileSync(t.savePath, buf);
    ok++;
  } catch (e) {
    fail++;
    failed.push(`${t.name}: ${e.message}`);
  }
}

// 简单并发：分批处理
async function run() {
  let i = 0;
  while (i < tasks.length) {
    const batch = tasks.slice(i, i + concurrency);
    await Promise.all(batch.map(downloadOne));
    i += batch.length;
    process.stdout.write(`\r  进度 ${i}/${tasks.length}  ok=${ok} skip=${skip} fail=${fail}      `);
  }
  console.log();
  if (failed.length > 0) {
    console.log("\n失败明细:");
    for (const f of failed.slice(0, 10)) console.log("  " + f);
    if (failed.length > 10) console.log(`  ... 共 ${failed.length} 条`);
  }
  console.log(`\n✓ 完成: ok=${ok} skip=${skip} fail=${fail}`);
}

await run();
