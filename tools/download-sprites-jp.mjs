#!/usr/bin/env node
/**
 * 下载「日文版」官方卡图，单独存放（不与中文版 cards/{set} 合并）。
 * 输出目录：opcgpro-web/public/cards/{setlower}-jp/{cardNumber}.png
 *
 * 使用：
 *   node tools/download-sprites-jp.mjs OP16
 *   node tools/download-sprites-jp.mjs OP16 --concurrency=5
 *   node tools/download-sprites-jp.mjs OP16 --force   # 强制重下（默认断点续传）
 *
 * 图源：日本万代官方 onepiece-cardgame.com/images/cardlist/card/{number}.png
 * 卡号来源：卡牌数据/{SET}.json 的 number 字段
 */

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, "..");

const setCode = (process.argv[2] || "OP16").toUpperCase();
const concurrency = parseInt(process.argv.find(a => a.startsWith("--concurrency="))?.split("=")[1] ?? "4", 10);
const force = process.argv.includes("--force");

const JP_BASE = "https://www.onepiece-cardgame.com/images/cardlist/card";

const srcPath = path.join(ROOT, "卡牌数据", `${setCode}.json`);
const outDir = path.join(ROOT, "opcgpro-web", "public", "cards", `${setCode.toLowerCase()}-jp`);

if (!fs.existsSync(srcPath)) {
  console.error(`!! 未找到卡集数据: ${srcPath}`);
  process.exit(1);
}
fs.mkdirSync(outDir, { recursive: true });

const cards = JSON.parse(fs.readFileSync(srcPath, "utf8"));
const seen = new Set();
const tasks = [];

for (const card of cards) {
  if (!card.number || seen.has(card.number)) continue;
  seen.add(card.number);
  tasks.push({
    url: `${JP_BASE}/${card.number}.png`,
    savePath: path.join(outDir, `${card.number}.png`),
    name: `${card.number}.png`,
  });
}

console.log(`==> ${setCode}（日文版）共 ${tasks.length} 张待下载`);
console.log(`    输出目录: ${outDir}`);
console.log(`    并发: ${concurrency}, 强制: ${force}`);

let ok = 0, skip = 0, fail = 0;
const failed = [];

async function fetchWithTimeout(url, ms) {
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), ms);
  try {
    return await fetch(url, {
      signal: ctrl.signal,
      headers: {
        "Referer": "https://www.onepiece-cardgame.com/",
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124.0 Safari/537.36",
      },
    });
  } finally {
    clearTimeout(timer);
  }
}

async function downloadOne(t) {
  if (!force && fs.existsSync(t.savePath)) {
    const st = fs.statSync(t.savePath);
    if (st.size > 1024) { skip++; return; }
  }
  // 单张最多重试 3 次，每次 15s 超时，避免某条连接挂死拖垮整批
  let lastErr = "";
  for (let attempt = 1; attempt <= 3; attempt++) {
    try {
      const res = await fetchWithTimeout(t.url, 15000);
      if (!res.ok) {
        if (res.status === 404) { fail++; failed.push(`${t.name}: HTTP 404（日版可能无此号）`); return; }
        lastErr = `HTTP ${res.status}`;
        continue;
      }
      const ct = res.headers.get("content-type") || "";
      if (!ct.startsWith("image/")) { fail++; failed.push(`${t.name}: 非图片响应 (${ct})`); return; }
      const buf = Buffer.from(await res.arrayBuffer());
      fs.writeFileSync(t.savePath, buf);
      ok++;
      return;
    } catch (e) {
      lastErr = e.name === "AbortError" ? "超时(15s)" : e.message;
    }
  }
  fail++;
  failed.push(`${t.name}: ${lastErr}`);
}

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
    for (const f of failed.slice(0, 20)) console.log("  " + f);
    if (failed.length > 20) console.log(`  ... 共 ${failed.length} 条`);
  }
  console.log(`\n✓ 完成: ok=${ok} skip=${skip} fail=${fail}`);
}

await run();
