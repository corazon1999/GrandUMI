// 图片缓存单独按两档 WebP 的内容维护，卡牌文字或卡池更新不再刷新全部卡图。
import { createHash } from "node:crypto";
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const statePath = join(webRoot, "scripts/card-image-cache-state.json");
const outputPath = join(webRoot, "src/data/cardImageVersions.ts");

export function cardImagePaths(manifest) {
  const paths = new Set();
  for (const sprites of Object.values(manifest)) {
    for (const source of sprites) {
      const match = source.match(/^\/cards\/(.+?)\.(?:png|jpe?g)(?:[?#].*)?$/i);
      if (match) paths.add(match[1]);
    }
  }
  return [...paths].sort();
}

export function imageFingerprint(thumbnail, display) {
  return createHash("sha256")
    .update(String(thumbnail.length)).update(":").update(thumbnail)
    .update(String(display.length)).update(":").update(display)
    .digest("hex").slice(0, 16);
}

export function updateImageCacheState(previous, fingerprints) {
  const revisions = { ...previous.revisions };
  for (const [path, fingerprint] of Object.entries(fingerprints)) {
    if (previous.fingerprints[path] !== fingerprint) revisions[path] = fingerprint;
  }
  // 不移除暂时退出卡表的历史图片，重新加入时仍可命中原来的缓存地址。
  return { ...previous, fingerprints: { ...previous.fingerprints, ...fingerprints }, revisions };
}

export function renderImageVersions(state) {
  return "// 此文件由 scripts/build-card-image-versions.mjs 生成，卡图版本与卡牌数据独立。\n"
    + `export const CARD_IMAGE_BASE_VERSION = ${JSON.stringify(state.baseVersion)};\n`
    + `export const CARD_IMAGE_REVISIONS: Readonly<Record<string, string>> = ${JSON.stringify(state.revisions, null, 2)};\n`;
}

export function verifyCardImageVersions(root = webRoot, { physical = false } = {}) {
  const state = JSON.parse(readFileSync(join(root, "scripts/card-image-cache-state.json"), "utf8"));
  if (state.schemaVersion !== 1 || !state.baseVersion || !state.fingerprints || !state.revisions) {
    throw new Error("卡图缓存版本记录格式无效。");
  }
  const manifest = JSON.parse(readFileSync(join(root, "public/data/imageManifest.json"), "utf8"));
  for (const path of cardImagePaths(manifest)) {
    if (!state.fingerprints[path]) throw new Error(`卡图缓存版本缺少 ${path}，请运行 node scripts/build-card-image-versions.mjs。`);
    if (physical) {
      const thumb = readFileSync(join(root, "public/cards-thumb", `${path}.webp`));
      const display = readFileSync(join(root, "public/cards-webp", `${path}.webp`));
      if (imageFingerprint(thumb, display) !== state.fingerprints[path]) {
        throw new Error(`卡图内容已变化但缓存版本未更新：${path}，请运行 node scripts/build-card-image-versions.mjs。`);
      }
    }
  }
  if (readFileSync(join(root, "src/data/cardImageVersions.ts"), "utf8") !== renderImageVersions(state)) {
    throw new Error("卡图缓存版本生成物与记录不一致，请重新生成。");
  }
  return cardImagePaths(manifest).length;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  if (process.argv.includes("--check")) {
    const count = verifyCardImageVersions(webRoot, { physical: process.argv.includes("--physical") });
    console.log(`卡图独立缓存版本校验通过：${count} 张。`);
  } else {
    const initializeIndex = process.argv.indexOf("--initialize");
    const baseVersion = initializeIndex >= 0 ? process.argv[initializeIndex + 1] : null;
    if (baseVersion && existsSync(statePath)) throw new Error("卡图缓存版本已初始化，拒绝覆盖历史版本。");
    if (!existsSync(statePath) && !baseVersion) throw new Error("首次生成需传入 --initialize 和当前线上卡图版本，保留玩家已有缓存。");
    const manifest = JSON.parse(readFileSync(join(webRoot, "public/data/imageManifest.json"), "utf8"));
    const fingerprints = {};
    for (const path of cardImagePaths(manifest)) {
      fingerprints[path] = imageFingerprint(
        readFileSync(join(webRoot, "public/cards-thumb", `${path}.webp`)),
        readFileSync(join(webRoot, "public/cards-webp", `${path}.webp`)),
      );
    }
    const state = baseVersion
      ? { schemaVersion: 1, baseVersion, fingerprints, revisions: {} }
      : updateImageCacheState(JSON.parse(readFileSync(statePath, "utf8")), fingerprints);
    writeFileSync(statePath, JSON.stringify(state, null, 2) + "\n");
    writeFileSync(outputPath, renderImageVersions(state));
    console.log(`卡图缓存版本已生成：${Object.keys(fingerprints).length} 张，独立修订 ${Object.keys(state.revisions).length} 张。`);
  }
}
