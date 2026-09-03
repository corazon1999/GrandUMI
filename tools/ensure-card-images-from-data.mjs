#!/usr/bin/env node
/**
 * 从已提交的卡牌数据 image 字段恢复 imageManifest 引用的正画原图。
 *
 * 这条路径只处理每个卡号的第一张（正画）；异画仍由 sync-card-set-images-cn.mjs 同步。
 * 默认输出到 CardImages，干净克隆可直接运行：
 *   node tools/ensure-card-images-from-data.mjs --only-missing
 * 测试服把输出指向其独立资源链接，避免改动正式服资源：
 *   node tools/ensure-card-images-from-data.mjs --output-root=opcgpro-web/public/cards --only-missing
 */

import { createHash } from "node:crypto";
import { readFile, readdir, mkdir, rename, stat, unlink, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const DATA_ROOT = path.join(ROOT, "opcgpro-web", "public", "data");
const MANIFEST_PATH = path.join(DATA_ROOT, "imageManifest.json");
const outputArg = process.argv.find(argument => argument.startsWith("--output-root="));
const OUTPUT_ROOT = path.resolve(ROOT, outputArg?.slice("--output-root=".length) || "CardImages");
const ONLY_MISSING = process.argv.includes("--only-missing");
const numbersArg = process.argv.find(argument => argument.startsWith("--numbers="));
const REQUESTED_NUMBERS = numbersArg
  ? new Set(numbersArg.slice("--numbers=".length).split(",").map(value => value.trim().toUpperCase()).filter(Boolean))
  : null;
const MAX_RETRY = 3;

function sha12(buffer) {
  return createHash("sha256").update(buffer).digest("hex").slice(0, 12);
}

function parseSprite(sprite) {
  if (typeof sprite !== "string" || !sprite.startsWith("/cards/")) return null;
  const url = new URL(sprite, "https://assets.invalid");
  const relativePath = decodeURIComponent(url.pathname.slice("/cards/".length));
  if (!relativePath || relativePath.includes("\0")) return null;
  return { relativePath, expectedDigest: url.searchParams.get("v") };
}

function assertInsideOutput(filePath) {
  const relative = path.relative(OUTPUT_ROOT, filePath);
  if (relative.startsWith("..") || path.isAbsolute(relative))
    throw new Error(`拒绝写入资源目录之外的路径：${filePath}`);
}

function looksLikeImage(buffer) {
  if (buffer.length < 1024) return false;
  const png = buffer.subarray(0, 8).equals(Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]));
  const jpeg = buffer[0] === 0xff && buffer[1] === 0xd8 && buffer.at(-2) === 0xff && buffer.at(-1) === 0xd9;
  const webp = buffer.subarray(0, 4).toString("ascii") === "RIFF"
    && buffer.subarray(8, 12).toString("ascii") === "WEBP";
  return png || jpeg || webp;
}

async function fetchImage(url, retry = 0) {
  try {
    const response = await fetch(url, {
      headers: {
        Referer: "https://www.onepiece-cardgame.cn/",
        "User-Agent": "GrandUMI card image recovery",
      },
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const buffer = Buffer.from(await response.arrayBuffer());
    if (!looksLikeImage(buffer)) throw new Error(`响应不是有效图片（${buffer.length} 字节）`);
    return buffer;
  } catch (error) {
    if (retry + 1 >= MAX_RETRY) throw error;
    await new Promise(resolve => setTimeout(resolve, 400 * (retry + 1)));
    return fetchImage(url, retry + 1);
  }
}

async function loadCards() {
  const result = new Map();
  const files = (await readdir(DATA_ROOT))
    .filter(file => file.endsWith(".json") && !file.startsWith("_")
      && file !== "allCards.json" && file !== "imageManifest.json");
  for (const file of files) {
    const cards = JSON.parse(await readFile(path.join(DATA_ROOT, file), "utf8"));
    if (!Array.isArray(cards)) continue;
    for (const card of cards)
      if (card?.number && card?.image) result.set(String(card.number).toUpperCase(), card);
  }
  return result;
}

async function existingIsUsable(filePath, expectedDigest) {
  try {
    const fileStat = await stat(filePath);
    if (!fileStat.isFile() || fileStat.size < 1024) return false;
    if (ONLY_MISSING) return true;
    if (!expectedDigest) return true;
    return sha12(await readFile(filePath)) === expectedDigest;
  } catch {
    return false;
  }
}

async function main() {
  const [manifest, cards] = await Promise.all([
    readFile(MANIFEST_PATH, "utf8").then(JSON.parse),
    loadCards(),
  ]);
  const tasks = [];
  for (const [number, sprites] of Object.entries(manifest)) {
    if (REQUESTED_NUMBERS && !REQUESTED_NUMBERS.has(number.toUpperCase())) continue;
    const sprite = Array.isArray(sprites) ? parseSprite(sprites[0]) : null;
    if (!sprite) continue;
    const card = cards.get(number.toUpperCase());
    if (!card?.image) continue;
    const destination = path.resolve(OUTPUT_ROOT, sprite.relativePath);
    assertInsideOutput(destination);
    if (await existingIsUsable(destination, sprite.expectedDigest)) continue;
    tasks.push({ number, image: card.image, destination, expectedDigest: sprite.expectedDigest });
  }

  let restored = 0;
  for (const task of tasks) {
    const buffer = await fetchImage(task.image);
    const actualDigest = sha12(buffer);
    if (task.expectedDigest && actualDigest !== task.expectedDigest)
      throw new Error(`${task.number} 官网图片摘要 ${actualDigest} 与清单 ${task.expectedDigest} 不一致`);
    await mkdir(path.dirname(task.destination), { recursive: true });
    const temporary = `${task.destination}.grandumi-download`;
    try {
      await writeFile(temporary, buffer);
      await rename(temporary, task.destination);
    } finally {
      await unlink(temporary).catch(() => {});
    }
    restored++;
    console.log(`  已恢复 ${task.number} -> ${path.relative(OUTPUT_ROOT, task.destination)}`);
  }

  console.log(`卡图正画恢复完成：检查 ${Object.keys(manifest).length} 个清单项，新增或修复 ${restored} 张。`);
}

await main();
