/**
 * 从仅限本地保存的卡面原文生成可提交的效果审计事实清单。
 *
 * 清单只保留卡号与派生能力，不写入卡面原文；运行时和 CI 只读取生成后的
 * `卡牌数据/_effect-audit.v1.json`，因此干净检出不依赖本地版权快照。
 */

import { readFile, readdir, writeFile } from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import {
  CARD_ROOT,
  ROOT,
  buildManifest,
  loadCanonicalCards,
  pretty,
  sha256,
  stable,
} from "./card-content-lib.mjs";

const ORIGINAL_CARD_ROOT = path.join(ROOT, "卡牌数据_含原文");
const OUTPUT_FILE = path.join(CARD_ROOT, "_effect-audit.v1.json");
const BASE_KEYWORDS = ["阻挡者", "速攻", "双重攻击", "可攻击活跃", "不可阻挡", "流放", "速攻：角色"];

if (!process.argv.includes("--write")) {
  console.error("此命令会更新效果审计事实清单；请显式传入 --write。");
  process.exit(2);
}

async function loadOriginalCards() {
  const cards = new Map();
  for (const file of (await readdir(ORIGINAL_CARD_ROOT)).filter((name) => name.endsWith(".json"))) {
    const parsed = JSON.parse(await readFile(path.join(ORIGINAL_CARD_ROOT, file), "utf8"));
    if (!Array.isArray(parsed)) continue;
    for (const card of parsed) {
      if (card?.number && !cards.has(card.number)) cards.set(card.number, card);
    }
  }
  return cards;
}

function isDeclaredBaseKeyword(text, keyword) {
  const escaped = keyword.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const anyKeyword = BASE_KEYWORDS
    .map((value) => value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"))
    .join("|");
  const annotation = "(?:（[^）]*）|\\([^)]*\\))";
  const prefix = `(?:【(?:${anyKeyword})】(?:\\s*${annotation})?\\s*)*`;
  return new RegExp(`(^|[。\\r\\n])\\s*${prefix}【${escaped}】(?=\\s*(?:（|\\(|【|$))`).test(text);
}

function expectedBaseAbilities(card) {
  const text = String(card.effectText ?? "");
  const abilities = BASE_KEYWORDS.filter((keyword) => isDeclaredBaseKeyword(text, keyword));
  // 早期卡面使用完整句描述同一规则，后续官方关键字名为【速攻：角色】。
  if (/^此角色可以在登场的回合中攻击角色/.test(text)) abilities.push("速攻：角色");
  if (/(^|[。\r\n])此角色无法攻击。/.test(text)) abilities.push("此角色无法攻击");
  if (card.number === "OP12-036") abilities.push("无法通过效果登场");
  if (["OP04-001", "OP04-039", "OP11-022"].includes(card.number)) abilities.push("此角色无法攻击");
  return [...new Set(abilities)].sort((left, right) => left.localeCompare(right, "zh-CN"));
}

function sameSet(left, right) {
  const a = [...new Set(left)].sort();
  const b = [...new Set(right)].sort();
  return a.length === b.length && a.every((value, index) => value === b[index]);
}

const loaded = await loadCanonicalCards();
if (loaded.errors.length) {
  for (const error of loaded.errors) console.error(`- ${error}`);
  process.exit(1);
}

const originals = await loadOriginalCards();
const currentCards = loaded.cards
  .map(({ __file, ...card }) => card)
  .sort((left, right) => left.number.localeCompare(right.number, "en"));
const baseAbilities = {};
const mismatches = [];
const publicRevealCards = [];
const cardsWithoutOriginalReference = [];
for (const card of currentCards) {
  const original = originals.get(card.number);
  const expected = original
    ? expectedBaseAbilities(original)
    : [...new Set(Array.isArray(card.abilities) ? card.abilities : [])].sort();
  if (!original) cardsWithoutOriginalReference.push(card.number);
  if (expected.length) baseAbilities[card.number] = expected;
  if (original && !sameSet(expected, Array.isArray(card.abilities) ? card.abilities : [])) mismatches.push(card.number);
  if (original && ["effectText", "effectEvent", "trigger"].some((field) =>
    String(original[field] ?? "").includes("公开"))) {
    publicRevealCards.push(card.number);
  }
}
if (mismatches.length) {
  throw new Error(`提交卡表的基础能力与本地原文不一致，拒绝刷新清单：${mismatches.join("、")}`);
}

const cardManifest = await buildManifest(loaded);
const payload = {
  schemaVersion: "grandumi.card-effect-audit.v1",
  cardContentSha256: cardManifest.contentSha256,
  cardCount: currentCards.length,
  cardsWithoutOriginalReference,
  baseAbilities,
  publicRevealCards,
};
const auditManifest = { ...payload, auditManifestSha256: sha256(stable(payload)) };
await writeFile(OUTPUT_FILE, pretty(auditManifest), "utf8");
console.log(
  `效果审计事实清单已更新：${currentCards.length} 张卡，`
  + `${Object.keys(baseAbilities).length} 张基础能力卡，${publicRevealCards.length} 张公开效果卡，`
  + `${cardsWithoutOriginalReference.length} 张卡暂无本地原文参考。`,
);
