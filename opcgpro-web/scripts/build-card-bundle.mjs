// 把 public/data/ 下所有卡集 JSON + imageManifest.json 合并成单个 allCards.json，
// 并按内容哈希生成 src/data/dataVersion.ts，用于卡组编辑器的「一次请求 + 永久缓存」加载。
//
// 触发时机：predev / prebuild 自动运行；也可手动 `npm run build:cards`。
// 注意：手动改了 public/data 里的卡数据后，需重跑本脚本（或 build）才会反映到单包。

import { createHash } from "node:crypto";
import { readFileSync, writeFileSync, readdirSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const __dirname = dirname(fileURLToPath(import.meta.url));
const root = join(__dirname, "..");
const dataDir = join(root, "public", "data");

// 1) 收集所有卡集文件（排除 manifest 与生成物），按文件名排序保证哈希稳定
const files = readdirSync(dataDir)
  .filter((f) => f.endsWith(".json") && !f.startsWith("_") && f !== "allCards.json" && f !== "imageManifest.json")
  .sort();

const cards = [];
for (const f of files) {
  const arr = JSON.parse(readFileSync(join(dataDir, f), "utf8"));
  if (Array.isArray(arr)) cards.push(...arr);
}

// 2) 读取图片 manifest（缺失则降级为空对象）
let manifest = {};
try {
  manifest = JSON.parse(readFileSync(join(dataDir, "imageManifest.json"), "utf8"));
} catch {
  console.warn("[build-card-bundle] 未找到 imageManifest.json，单包将不含异画信息");
}

let playability = { schemaVersion: "grandumi.card-playability.v1", cards: [] };
try {
  playability = JSON.parse(readFileSync(join(dataDir, "_playability.v1.json"), "utf8"));
} catch (error) {
  if (error?.code !== "ENOENT") throw error;
  // 兼容真正没有状态文件的旧卡表；文件存在但损坏时必须失败关闭。
}
if (playability?.schemaVersion !== "grandumi.card-playability.v1" || !Array.isArray(playability.cards)) {
  throw new Error("_playability.v1.json 格式无效");
}
const knownNumbers = new Set(cards.map((card) => card.number));
const pendingNumbers = playability.cards.map((entry) => {
  if (!entry || typeof entry !== "object" || typeof entry.number !== "string" || entry.state !== "pending") {
    throw new Error("_playability.v1.json 只能包含合法的 pending 卡牌记录");
  }
  if (!knownNumbers.has(entry.number)) {
    throw new Error(`_playability.v1.json 引用了不存在的卡牌：${entry.number}`);
  }
  return entry.number;
});
if (new Set(pendingNumbers).size !== pendingNumbers.length) {
  throw new Error("_playability.v1.json 包含重复卡号");
}
if (pendingNumbers.some((number, index) => index > 0 && pendingNumbers[index - 1] >= number)) {
  throw new Error("_playability.v1.json 卡号必须严格升序排列");
}

// 3) 算内容哈希（卡 + manifest），作为版本号
const payload = JSON.stringify({ cards, manifest, playability });
const version = createHash("sha256").update(payload).digest("hex").slice(0, 12);

// 4) 写出单包与版本文件
const bundle = JSON.stringify({ version, manifest, playability, cards });
writeFileSync(join(dataDir, "allCards.json"), bundle);

const versionTs =
  "// 此文件由 scripts/build-card-bundle.mjs 自动生成，请勿手动修改。\n" +
  `export const DATA_VERSION = "${version}";\n`;
writeFileSync(join(root, "src", "data", "dataVersion.ts"), versionTs);

console.log(
  `[build-card-bundle] ${files.length} 个卡集 / ${cards.length} 张卡 → allCards.json` +
    ` (${(bundle.length / 1024 / 1024).toFixed(2)}MB, version=${version})`,
);
