#!/usr/bin/env node
/**
 * 从简中官网同步指定卡集的正画与异画，并更新前端图片清单。
 *
 * 用法：
 *   node tools/sync-card-set-images-cn.mjs OP16
 *
 * 数据源：简中官网卡表接口与图片 CDN。
 */

import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, "..");
const API_BASE = "https://onepieceserve.windoent.com";
const REFERER = "https://www.onepiece-cardgame.cn/";
const USER_AGENT =
  "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124.0 Safari/537.36";
const PAGE_SIZE = 500;
const CONCURRENCY = 6;
const MAX_RETRY = 3;

const setCode = process.argv[2]?.toUpperCase();
if (!setCode || !/^[A-Z]+\d+$/.test(setCode)) {
  console.error("用法：node tools/sync-card-set-images-cn.mjs <卡集编号>");
  console.error("示例：node tools/sync-card-set-images-cn.mjs OP16");
  process.exit(1);
}

const outputDir = path.join(ROOT, "CardImages", setCode.toLowerCase());
const manifestPath = path.join(
  ROOT,
  "opcgpro-web",
  "public",
  "data",
  "imageManifest.json",
);

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function fetchWithRetry(url, responseType = "json", retry = 0) {
  try {
    const response = await fetch(url, {
      headers: { Referer: REFERER, "User-Agent": USER_AGENT },
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    return responseType === "buffer"
      ? Buffer.from(await response.arrayBuffer())
      : await response.json();
  } catch (error) {
    if (retry >= MAX_RETRY) throw error;
    await sleep(500 * (retry + 1));
    return fetchWithRetry(url, responseType, retry + 1);
  }
}

async function scanCardRows() {
  const firstUrl = `${API_BASE}/cardList/cardlist/weblist?limit=${PAGE_SIZE}&page=0`;
  const first = await fetchWithRetry(firstUrl);
  if (first.code !== 0 || !first.page) {
    throw new Error(`卡表接口返回异常：${first.msg ?? "缺少分页数据"}`);
  }

  // 官网分页同时兼容从 0 和从 1 开始；最终按记录 ID 去重。
  const totalPages = Number(first.page.totalPage) || 1;
  const pages = [first.page.list ?? []];
  for (let page = 1; page <= totalPages; page++) {
    const url = `${API_BASE}/cardList/cardlist/weblist?limit=${PAGE_SIZE}&page=${page}`;
    const data = await fetchWithRetry(url);
    if (data.code === 0 && data.page?.list) pages.push(data.page.list);
  }

  const byId = new Map();
  for (const item of pages.flat()) {
    if (!String(item.cardNumber ?? "").startsWith(`${setCode}-`)) continue;
    byId.set(String(item.id), item);
  }
  return [...byId.values()].sort((a, b) =>
    String(a.cardNumber).localeCompare(String(b.cardNumber), "en", {
      numeric: true,
    }),
  );
}

async function mapConcurrent(items, worker) {
  let cursor = 0;
  const results = new Array(items.length);
  async function runWorker() {
    while (cursor < items.length) {
      const index = cursor++;
      results[index] = await worker(items[index], index);
    }
  }
  await Promise.all(
    Array.from({ length: Math.min(CONCURRENCY, items.length) }, runWorker),
  );
  return results;
}

async function loadImageTasks(rows) {
  const tasks = await mapConcurrent(rows, async (row, index) => {
    const url = `${API_BASE}/cardList/cardlist/webInfo/${row.id}`;
    const data = await fetchWithRetry(url);
    if (data.code !== 0 || !data.info?.cardImg) {
      throw new Error(
        `${row.cardNumber} 详情获取失败：${data.msg ?? "缺少图片地址"}`,
      );
    }

    const imageUrl = data.info.cardImg;
    const extension =
      imageUrl.match(/\.(png|jpg|jpeg|webp)(?:\?|$)/i)?.[1]?.toLowerCase() ??
      "png";
    const fullNumber = String(row.cardNumber).toUpperCase();
    process.stdout.write(`\r读取官网卡图地址 ${index + 1}/${rows.length}`);
    return {
      cardNumber: fullNumber,
      imageUrl,
      filename: `${fullNumber}.${extension}`,
    };
  });
  process.stdout.write("\n");

  // 同一图片记录可能在官网不同商品中重复出现，以完整卡号唯一保留。
  return [...new Map(tasks.map((task) => [task.cardNumber, task])).values()];
}

async function downloadImages(tasks) {
  await fs.mkdir(outputDir, { recursive: true });
  let completed = 0;
  await mapConcurrent(tasks, async (task) => {
    const buffer = await fetchWithRetry(task.imageUrl, "buffer");
    if (buffer.length < 1024) {
      throw new Error(`${task.cardNumber} 图片内容异常，仅 ${buffer.length} 字节`);
    }
    await fs.writeFile(path.join(outputDir, task.filename), buffer);
    completed++;
    process.stdout.write(`\r下载简中官网卡图 ${completed}/${tasks.length}`);
  });
  process.stdout.write("\n");
}

function canonicalNumber(filename) {
  const match = filename.match(
    new RegExp(`^(${setCode}-\\d{3})(?:_[A-Za-z0-9-]+)?\\.(?:png|jpg|jpeg|webp)$`, "i"),
  );
  return match?.[1]?.toUpperCase() ?? null;
}

function spriteSort(a, b) {
  const aBase = !/_[A-Za-z0-9-]+\.[^.]+$/i.test(a);
  const bBase = !/_[A-Za-z0-9-]+\.[^.]+$/i.test(b);
  if (aBase !== bBase) return aBase ? -1 : 1;
  return a.localeCompare(b, "en", { numeric: true });
}

async function updateManifest() {
  const manifest = JSON.parse(await fs.readFile(manifestPath, "utf8"));
  const files = await fs.readdir(outputDir);
  const groups = new Map();

  for (const filename of files) {
    const number = canonicalNumber(filename);
    if (!number) continue;
    const sprites = groups.get(number) ?? [];
    sprites.push(`/cards/${setCode.toLowerCase()}/${filename}`);
    groups.set(number, sprites);
  }

  const setMatch = setCode.match(/^([A-Z]+)(\d+)$/);
  const setFamily = setMatch?.[1] ?? setCode;
  const setIndex = Number(setMatch?.[2] ?? 0);
  const setEntries = [...groups.entries()]
    .sort(([a], [b]) => a.localeCompare(b, "en", { numeric: true }))
    .map(([number, sprites]) => [number, sprites.sort(spriteSort)]);

  // 保持原清单顺序，只在同系列的下一卡集前插入当前卡集，避免重排其他卡集。
  const updatedManifest = {};
  let inserted = false;
  for (const [key, sprites] of Object.entries(manifest)) {
    if (key.startsWith(`${setCode}-`)) continue;
    const keySet = key.split("-")[0];
    const keyMatch = keySet.match(/^([A-Z]+)(\d+)$/);
    if (
      !inserted &&
      keyMatch?.[1] === setFamily &&
      Number(keyMatch[2]) > setIndex
    ) {
      for (const [number, setSprites] of setEntries) {
        updatedManifest[number] = setSprites;
      }
      inserted = true;
    }
    updatedManifest[key] = sprites;
  }
  if (!inserted) {
    for (const [number, setSprites] of setEntries) {
      updatedManifest[number] = setSprites;
    }
  }
  await fs.writeFile(
    manifestPath,
    `${JSON.stringify(updatedManifest, null, 2)}\n`,
    "utf8",
  );

  const withVariants = [...groups.values()].filter(
    (sprites) => sprites.length > 1,
  );
  const variantCount = withVariants.reduce(
    (sum, sprites) => sum + sprites.length - 1,
    0,
  );
  return {
    cardCount: groups.size,
    variantCardCount: withVariants.length,
    variantCount,
  };
}

async function main() {
  console.log(`开始从简中官网同步 ${setCode} 卡图……`);
  const rows = await scanCardRows();
  if (rows.length === 0) throw new Error(`简中官网没有找到 ${setCode}`);

  const tasks = await loadImageTasks(rows);
  await downloadImages(tasks);
  const summary = await updateManifest();

  console.log(
    `同步完成：${summary.cardCount} 个卡号，` +
      `${summary.variantCardCount} 个卡号含异画，共 ${summary.variantCount} 张异画。`,
  );
}

main().catch((error) => {
  console.error(`同步失败：${error.message}`);
  process.exit(1);
});
