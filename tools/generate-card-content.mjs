import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import {
  CARD_ROOT,
  FRONTEND_CARD_ROOT,
  MANIFEST_FILE,
  REGISTRY_FILE,
  buildEffectRegistry,
  buildIndex,
  buildManifest,
  loadCanonicalCards,
  pretty,
} from "./card-content-lib.mjs";

if (!process.argv.includes("--write")) {
  console.error("此命令会机械同步派生文件；请显式传入 --write。");
  process.exit(2);
}

const loaded = await loadCanonicalCards();
if (loaded.errors.length) {
  for (const error of loaded.errors) console.error(`- ${error}`);
  process.exit(1);
}
const manifest = await buildManifest(loaded);
const registry = await buildEffectRegistry(loaded, manifest);
await mkdir(FRONTEND_CARD_ROOT, { recursive: true });
for (const file of loaded.files) {
  await writeFile(path.join(FRONTEND_CARD_ROOT, file), await readFile(path.join(CARD_ROOT, file)));
}
await writeFile(path.join(CARD_ROOT, MANIFEST_FILE), pretty(manifest), "utf8");
await writeFile(path.join(CARD_ROOT, REGISTRY_FILE), pretty(registry), "utf8");
await writeFile(path.join(CARD_ROOT, "_index.json"), pretty(buildIndex(loaded)), "utf8");
console.log(`卡牌派生内容已同步：${manifest.files.length} 个卡集、${manifest.totalCards} 张卡、内容 ${manifest.contentSha256}。`);
console.log(`效果 registry：${registry.implementationCardCount} 张卡有实现登记，内建关键词 ${registry.builtinMetadataCards.length}，未登记标签 ${registry.unresolvedTaggedCards.length}，孤儿 ${registry.orphanImplementations.length}，重复脚本 ${registry.duplicateScripted.length}。`);
