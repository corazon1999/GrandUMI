import { execFileSync } from "node:child_process";
import { readFile } from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { CARD_ROOT, loadCanonicalCards } from "./card-content-lib.mjs";

const baseIndex = process.argv.indexOf("--base");
const base = baseIndex >= 0 ? process.argv[baseIndex + 1] : null;
const current = await loadCanonicalCards();
const currentCards = new Map(current.cards.map(({ __file, ...card }) => [card.number, { ...card, __file }]));
const beforeCards = new Map();

for (const file of current.files) {
  let text;
  try {
    text = base
      ? execFileSync("git", ["show", `${base}:卡牌数据/${file}`], { cwd: path.dirname(CARD_ROOT), encoding: "utf8" })
      : await readFile(path.join(path.dirname(CARD_ROOT), "opcgpro-web", "public", "data", file), "utf8");
  } catch { continue; }
  for (const card of JSON.parse(text)) beforeCards.set(card.number, { ...card, __file: file });
}

const added = [...currentCards.keys()].filter((number) => !beforeCards.has(number)).sort();
const removed = [...beforeCards.keys()].filter((number) => !currentCards.has(number)).sort();
const changed = [];
for (const [number, card] of currentCards) {
  const before = beforeCards.get(number);
  if (!before) continue;
  const fields = [...new Set([...Object.keys(before), ...Object.keys(card)])]
    .filter((field) => field !== "__file" && JSON.stringify(before[field]) !== JSON.stringify(card[field]));
  if (fields.length) changed.push({ number, fields });
}

console.log(`# 卡牌内容差异${base ? `（相对 ${base}）` : "（canonical ↔ 前端镜像）"}\n`);
console.log(`- 新增：${added.length}`);
console.log(`- 删除：${removed.length}`);
console.log(`- 修改：${changed.length}\n`);
if (added.length) console.log(`## 新增\n\n${added.map((number) => `- ${number}`).join("\n")}\n`);
if (removed.length) console.log(`## 删除\n\n${removed.map((number) => `- ${number}`).join("\n")}\n`);
if (changed.length) console.log(`## 字段变化\n\n${changed.map((entry) => `- ${entry.number}: ${entry.fields.join("、")}`).join("\n")}\n`);
if (!added.length && !removed.length && !changed.length) console.log("无差异。");
