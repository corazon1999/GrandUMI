#!/usr/bin/env node
/**
 * 对比简中官网卡表与本地结构化卡牌数据。
 *
 * 默认只读输出摘要；--strict 在官网有而本地无的标准卡号存在时返回非 0。
 */

import { readFile, readdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const DATA_DIR = path.join(ROOT, "卡牌数据");
const API = "https://webadmin.windoent.com/op-public/cardList/cardlist/weblist?limit=10000&page=1";
const STRICT = process.argv.includes("--strict");

function canonicalNumber(value) {
  return String(value ?? "").trim().toUpperCase().match(/^([A-Z]{1,5}\d{0,2}-\d{3,})/)?.[1] ?? null;
}

async function loadLocalNumbers() {
  const numbers = new Set();
  const files = (await readdir(DATA_DIR)).filter(
    (name) => name.endsWith(".json") && name !== "_index.json",
  );
  for (const file of files) {
    const cards = JSON.parse(await readFile(path.join(DATA_DIR, file), "utf8"));
    if (!Array.isArray(cards)) continue;
    for (const card of cards) {
      const number = canonicalNumber(card?.number);
      if (number) numbers.add(number);
    }
  }
  return numbers;
}

const response = await fetch(API, {
  headers: {
    Referer: "https://www.onepiece-cardgame.cn/",
    "User-Agent": "GrandUMI card coverage audit",
  },
});
if (!response.ok) throw new Error(`官网卡表请求失败：HTTP ${response.status}`);
const payload = await response.json();
if (payload.code !== 0 || !Array.isArray(payload.page?.list)) {
  throw new Error(`官网卡表返回异常：${payload.msg ?? "缺少列表"}`);
}

const official = new Set(
  payload.page.list.map((row) => canonicalNumber(row.cardNumber)).filter(Boolean),
);
const local = await loadLocalNumbers();
const missing = [...official].filter((number) => !local.has(number)).sort((a, b) =>
  a.localeCompare(b, "en", { numeric: true }),
);
const groups = new Map();
for (const number of missing) {
  const set = number.split("-")[0];
  const values = groups.get(set) ?? [];
  values.push(number);
  groups.set(set, values);
}

console.log(`官网唯一标准卡号：${official.size}`);
console.log(`本地唯一标准卡号：${local.size}`);
console.log(`官网有、本地无：${missing.length}`);
for (const [set, values] of [...groups].sort(([a], [b]) => a.localeCompare(b, "en", { numeric: true }))) {
  console.log(`  ${set}（${values.length}）：${values.join("、")}`);
}

if (STRICT && missing.length > 0) process.exitCode = 1;
