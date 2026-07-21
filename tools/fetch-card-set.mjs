#!/usr/bin/env node
/**
 * 从官方 API 抓取指定卡集 JSON，输出到 卡牌数据/{SET}.json
 *
 * 使用：
 *   node tools/fetch-card-set.mjs OP16
 *   node tools/fetch-card-set.mjs OP16 --out=./tmp.json
 *
 * 数据源：
 *   列表：https://onepieceserve.windoent.com/cardList/cardlist/weblist?limit=N&page=K
 *   详情：https://onepieceserve.windoent.com/cardList/cardlist/webInfo/{id}
 *
 * 输出字段与现有 卡牌数据/OP15.json 完全一致（便于直接替换/合并）。
 */

import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT       = path.resolve(__dirname, "..");
const OUT_DIR    = path.join(ROOT, "卡牌数据");

const API_BASE   = "https://onepieceserve.windoent.com";
const REFERER    = "https://www.onepiece-cardgame.cn/";
const UA         = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124.0 Safari/537.36";
// 官网列表接口经本地代理传输超大响应时可能被中途切断；500 条/页既能
// 明显减少全量扫描请求数，又能把单次响应控制在稳定范围内。
const PAGE_SIZE  = 500;
const REQ_DELAY  = 100; // ms
const MAX_RETRY  = 3;

// ── 颜色 / 类型 映射 ──────────────────────────────────────────────────────
const COLOR_MAP = {
  "红": "红",
  "绿": "绿",
  "蓝": "蓝",
  "紫": "紫",
  "黑": "黑",
  "黄": "黄",
};
const TYPE_MAP = {
  "领袖": "领航",  // 现有数据用"领航"
};

function mapColor(raw) {
  if (!raw) return "";
  return raw.split("/").map(c => COLOR_MAP[c] ?? c).join("/");
}
function mapType(raw) {
  return TYPE_MAP[raw] ?? raw;
}
function mapProperty(arr) {
  if (Array.isArray(arr)) return arr.join("/");
  return arr ?? "";
}
function mapCounter(raw) {
  // API "-" / null / "" → ""；数字串 → "反击+N"（与现有数据一致）
  if (!raw || raw === "-") return "";
  // 直接是数字字符串："1000" → "反击+1000"
  if (/^\d+$/.test(raw)) return `反击+${raw}`;
  return raw;
}

// ── HTTP 工具 ─────────────────────────────────────────────────────────────
async function fetchJson(url, retry = 0) {
  try {
    const res = await fetch(url, { headers: { "User-Agent": UA, "Referer": REFERER } });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return await res.json();
  } catch (e) {
    if (retry < MAX_RETRY) {
      await sleep(500 * (retry + 1));
      return fetchJson(url, retry + 1);
    }
    throw e;
  }
}

function sleep(ms) { return new Promise(r => setTimeout(r, ms)); }

// ── 主流程 ────────────────────────────────────────────────────────────────
async function fetchCardSet(setCode) {
  console.log(`==> 抓取卡集 ${setCode}`);
  const setPrefix = setCode.toUpperCase() + "-";

  // 1. 翻所有页找出该卡集的所有 (id, cardNumber)
  //    同一 cardNumber 在 weblist 中可能有多个 id（正画、异画或不同商品重复收录）。
  //    先保留全部候选，详情阶段优先选择卡号无后缀的正画记录。
  console.log("[1/2] 扫描卡牌列表...");
  const byNumber = new Map(); // cardNumber -> id[]
  let page = 0;
  let totalPage = 1;
  let dupCount = 0;
  while (page < totalPage) {
    const url = `${API_BASE}/cardList/cardlist/weblist?limit=${PAGE_SIZE}&page=${page}`;
    const data = await fetchJson(url);
    if (data.code !== 0) throw new Error(`API 返回错误：${data.msg}`);
    totalPage = data.page.totalPage;
    for (const item of data.page.list) {
      if (item.cardNumber && item.cardNumber.startsWith(setPrefix)) {
        const canonicalNumber = item.cardNumber.replace(/_[A-Za-z0-9-]+$/i, "");
        const ids = byNumber.get(canonicalNumber) ?? [];
        if (ids.length > 0) dupCount++;
        ids.push(item.id);
        byNumber.set(canonicalNumber, ids);
      }
    }
    process.stdout.write(`\r  扫描 ${page + 1}/${totalPage} 页，唯一 ${byNumber.size}，重复 ${dupCount}      `);
    page++;
    await sleep(REQ_DELAY);
  }
  console.log();
  const idsOfSet = [...byNumber.entries()].map(([number, ids]) => ({ ids, number }));

  if (idsOfSet.length === 0) {
    console.error(`!! 未在 API 中找到 ${setCode} 卡牌`);
    process.exit(2);
  }

  // 2. 逐张拉详情
  console.log(`[2/2] 拉取 ${idsOfSet.length} 张卡详情...`);
  const cards = [];
  for (let i = 0; i < idsOfSet.length; i++) {
    const { ids, number } = idsOfSet[i];
    const candidates = [];
    for (const id of ids) {
      const url = `${API_BASE}/cardList/cardlist/webInfo/${id}`;
      const data = await fetchJson(url);
      if (data.code !== 0 || !data.info) {
        console.warn(`\n  [警告] ${number} (id=${id}) 拉取失败：${data.msg ?? "无 info"}`);
      } else {
        candidates.push(data.info);
      }
      await sleep(REQ_DELAY);
    }
    if (candidates.length === 0) continue;
    const escaped = number.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    const baseImage = new RegExp(`${escaped}\\.(?:png|jpg|jpeg|webp)(?:\\?|$)`, "i");
    candidates.sort((a, b) => Number(baseImage.test(b.cardImg ?? "")) - Number(baseImage.test(a.cardImg ?? "")));
    cards.push(toSchemaCard({ ...candidates[0], cardNumber: number }));
    process.stdout.write(`\r  ${i + 1}/${idsOfSet.length}  ${number}                `);
  }
  console.log();

  // 3. 按 cardNumber 字典序稳定排序
  cards.sort((a, b) => a.number.localeCompare(b.number, "en"));

  return cards;
}

function toSchemaCard(info) {
  return {
    number:      info.cardNumber       ?? "",
    name:        info.cardName         ?? "",
    color:       mapColor(info.cardColor),
    type:        mapType(info.cardType),
    property:    mapProperty(info.cardAttribute),
    power:       info.cardPower        ?? "",
    cost:        info.cardLife         ?? "",
    keyWords:    info.cardFeatures     ?? "",
    counter:     mapCounter(info.cardAttack),
    effectText:  info.cardTextDesc     ?? "",
    effectEvent: "",
    rarity:      info.cardRarity       ?? "",
    subscript:   info.subscript        ?? 0,
    trigger:     info.cardTrigger      ?? "",
    set:         info.cardOfferType    ?? "",
    image:       info.cardImg          ?? "",
    cartograph:  info.cardCartograph   ?? "",
  };
}

// ── 入口 ──────────────────────────────────────────────────────────────────
const args = process.argv.slice(2);
const setCode = args.find(a => !a.startsWith("--"));
const outArg  = args.find(a => a.startsWith("--out="));

if (!setCode) {
  console.error("Usage: node tools/fetch-card-set.mjs <SET_CODE> [--out=path]");
  console.error("Example: node tools/fetch-card-set.mjs OP16");
  process.exit(1);
}

const outPath = outArg
  ? outArg.slice("--out=".length)
  : path.join(OUT_DIR, `${setCode.toUpperCase()}.json`);

const cards = await fetchCardSet(setCode);
await fs.mkdir(path.dirname(outPath), { recursive: true });
await fs.writeFile(outPath, JSON.stringify(cards, null, 2), "utf8");
console.log(`\n✓ 已写入 ${outPath}（${cards.length} 张）`);
