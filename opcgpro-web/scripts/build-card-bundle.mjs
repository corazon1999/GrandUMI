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

const EXCLUDE = new Set(["allCards.json", "imageManifest.json"]);

// 1) 收集所有卡集文件（排除 manifest 与生成物），按文件名排序保证哈希稳定
const files = readdirSync(dataDir)
  .filter((f) => f.endsWith(".json") && !EXCLUDE.has(f))
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

// 3) 算内容哈希（卡 + manifest），作为版本号
const payload = JSON.stringify({ cards, manifest });
const version = createHash("sha256").update(payload).digest("hex").slice(0, 12);

// 4) 写出单包与版本文件
const bundle = JSON.stringify({ version, manifest, cards });
writeFileSync(join(dataDir, "allCards.json"), bundle);

const versionTs =
  "// 此文件由 scripts/build-card-bundle.mjs 自动生成，请勿手动修改。\n" +
  `export const DATA_VERSION = "${version}";\n`;
writeFileSync(join(root, "src", "data", "dataVersion.ts"), versionTs);

console.log(
  `[build-card-bundle] ${files.length} 个卡集 / ${cards.length} 张卡 → allCards.json` +
    ` (${(bundle.length / 1024 / 1024).toFixed(2)}MB, version=${version})`,
);
