#!/usr/bin/env node
/**
 * 从简中官网将“官网有、本地无”的标准卡号增量合并到三份数据副本。
 * 默认 dry-run；传入 --write 才写盘。可用 --sets=OP01,ST19 限定卡集。
 */

import { access, readFile, readdir, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const API_BASE = "https://webadmin.windoent.com/op-public";
const WRITE = process.argv.includes("--write");
const setsArg = process.argv.find((arg) => arg.startsWith("--sets="));
const SETS = setsArg
  ? new Set(setsArg.slice("--sets=".length).split(",").map((value) => value.trim().toUpperCase()).filter(Boolean))
  : null;
const TARGET_DIRS = [
  path.join(ROOT, "卡牌数据_含原文"),
  path.join(ROOT, "卡牌数据"),
  path.join(ROOT, "opcgpro-web", "public", "data"),
];

function canonicalNumber(value) {
  return String(value ?? "").trim().toUpperCase().match(/^([A-Z]{1,5}\d{0,2}-\d{3,})/)?.[1] ?? null;
}

function setOf(number) {
  return number.split("-")[0];
}

function mapScalar(value) {
  return value == null || value === "-" ? "" : String(value);
}

function mapList(value) {
  const text = Array.isArray(value) ? value.join("/") : mapScalar(value);
  return text.replace(/[，,]/g, "/");
}

function mapCounter(value) {
  const match = mapScalar(value).match(/\d+/);
  return match?.[0] ?? "";
}

function mapRarity(value) {
  const text = mapScalar(value);
  return text.match(/[（(]([A-Z]+)[）)]/)?.[1] ?? text;
}

function toCard(info, number) {
  return {
    number,
    name: mapScalar(info.cardName),
    color: mapList(info.cardColor),
    type: info.cardType === "领袖" ? "领航" : mapScalar(info.cardType),
    property: mapList(info.cardAttribute),
    power: mapScalar(info.cardPower),
    cost: mapScalar(info.cardLife),
    keyWords: mapList(info.cardFeatures),
    counter: mapCounter(info.cardAttack),
    effectText: mapScalar(info.cardTextDesc),
    effectEvent: "",
    rarity: mapRarity(info.cardRarity),
    subscript: info.subscript ?? 0,
    trigger: mapScalar(info.cardTrigger),
    set: mapScalar(info.cardOfferType),
    image: mapScalar(info.cardImg),
    cartograph: mapScalar(info.cardCartograph),
  };
}

async function fetchJson(url) {
  const response = await fetch(url, {
    headers: {
      Referer: "https://www.onepiece-cardgame.cn/",
      "User-Agent": "GrandUMI missing-card importer",
    },
  });
  if (!response.ok) throw new Error(`HTTP ${response.status}: ${url}`);
  return response.json();
}

async function localNumbers() {
  const result = new Set();
  const dir = path.join(ROOT, "卡牌数据");
  for (const file of (await readdir(dir)).filter((name) => name.endsWith(".json") && name !== "_index.json")) {
    const cards = JSON.parse(await readFile(path.join(dir, file), "utf8"));
    if (!Array.isArray(cards)) continue;
    for (const card of cards) {
      const number = canonicalNumber(card?.number);
      if (number) result.add(number);
    }
  }
  return result;
}

const listPayload = await fetchJson(`${API_BASE}/cardList/cardlist/weblist?limit=10000&page=1`);
if (listPayload.code !== 0 || !Array.isArray(listPayload.page?.list)) {
  throw new Error(`官网卡表返回异常：${listPayload.msg ?? "缺少列表"}`);
}

const local = await localNumbers();
const rowsByNumber = new Map();
for (const row of listPayload.page.list) {
  const number = canonicalNumber(row.cardNumber);
  if (!number || local.has(number) || (SETS && !SETS.has(setOf(number)))) continue;
  const file = path.join(ROOT, "卡牌数据", `${setOf(number)}.json`);
  try { await access(file); } catch { continue; }
  const rows = rowsByNumber.get(number) ?? [];
  rows.push(row);
  rowsByNumber.set(number, rows);
}

const imported = [];
for (const [number, rows] of [...rowsByNumber].sort(([a], [b]) => a.localeCompare(b, "en", { numeric: true }))) {
  const candidates = [];
  for (const row of rows) {
    const payload = await fetchJson(`${API_BASE}/cardList/cardlist/webInfo/${row.id}`);
    if (payload.code === 0 && payload.info) candidates.push({ row, info: payload.info });
  }
  if (candidates.length === 0) throw new Error(`${number} 没有可用详情`);
  const escaped = number.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const baseImage = new RegExp(`${escaped}\\.(?:png|jpe?g|webp)(?:\\?|$)`, "i");
  candidates.sort((a, b) => {
    const score = (candidate) =>
      (String(candidate.row.cardNumber).toUpperCase() === number ? 100 : 0) +
      (baseImage.test(candidate.info.cardImg ?? "") ? 10 : 0);
    return score(b) - score(a);
  });
  imported.push(toCard(candidates[0].info, number));
  console.log(`  ${number} ${candidates[0].info.cardName ?? ""}`);
}

console.log(`待合并：${imported.length} 张（${WRITE ? "写盘" : "dry-run"}）`);
if (!WRITE || imported.length === 0) process.exit(0);

for (const dir of TARGET_DIRS) {
  const bySet = new Map();
  for (const card of imported) {
    const values = bySet.get(setOf(card.number)) ?? [];
    values.push(card);
    bySet.set(setOf(card.number), values);
  }
  for (const [set, additions] of bySet) {
    const file = path.join(dir, `${set}.json`);
    const cards = JSON.parse(await readFile(file, "utf8"));
    const existing = new Set(cards.map((card) => card.number));
    cards.push(...additions.filter((card) => !existing.has(card.number)));
    cards.sort((a, b) => a.number.localeCompare(b.number, "en", { numeric: true }));
    await writeFile(file, `${JSON.stringify(cards, null, 2)}\n`, "utf8");
  }
}

console.log(`已将 ${imported.length} 张卡合并到原文、服务端与前端三份数据。`);
