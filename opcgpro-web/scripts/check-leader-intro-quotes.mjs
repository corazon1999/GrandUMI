import { readFile, readdir } from "node:fs/promises";
import path from "node:path";
import process from "node:process";

const projectRoot = process.cwd();
const cardDataDir = path.join(projectRoot, "public", "data");
const quoteDataPath = path.join(projectRoot, "src", "data", "leaderIntroQuotes.json");

const quoteData = JSON.parse(await readFile(quoteDataPath, "utf8"));
const byName = quoteData.byName ?? {};
const byNumber = quoteData.byNumber ?? {};
const fallback = typeof quoteData.fallback === "string" ? quoteData.fallback.trim() : "";
const errors = [];
const leaders = [];

if (!fallback) errors.push("缺少非空的 fallback 台词");

for (const fileName of await readdir(cardDataDir)) {
  if (!fileName.endsWith(".json") || fileName === "imageManifest.json") continue;

  const filePath = path.join(cardDataDir, fileName);
  let cards;
  try {
    cards = JSON.parse(await readFile(filePath, "utf8"));
  } catch {
    continue;
  }

  if (!Array.isArray(cards)) continue;
  for (const card of cards) {
    if (card?.rarity === "L") leaders.push(card);
  }
}

for (const leader of leaders) {
  const quote = byNumber[leader.number] ?? byName[leader.name];
  if (typeof quote !== "string" || !quote.trim()) {
    errors.push(`${leader.number} ${leader.name} 缺少专属台词映射`);
    continue;
  }
  if (quote.trim().length > 32) {
    errors.push(`${leader.number} ${leader.name} 的台词超过 32 个字符`);
  }
}

for (const [number, quote] of Object.entries(byNumber)) {
  if (!leaders.some((leader) => leader.number === number)) {
    errors.push(`卡号覆盖 ${number} 未对应当前 Leader`);
  }
  if (typeof quote !== "string" || !quote.trim()) {
    errors.push(`卡号覆盖 ${number} 的台词为空`);
  }
}

if (errors.length > 0) {
  console.error("Leader 开场台词检查失败：");
  for (const error of errors) console.error(`- ${error}`);
  process.exit(1);
}

const uniqueNames = new Set(leaders.map((leader) => leader.name));
console.log(`Leader 开场台词检查通过：${leaders.length} 张 Leader，${uniqueNames.size} 个角色，${Object.keys(byNumber).length} 个卡号剧情覆盖。`);
