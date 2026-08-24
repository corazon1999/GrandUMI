#!/usr/bin/env node
/**
 * 从公开的「Dr.OPCG蛋头岛卡牌翻译」腾讯文档读取已填写的卡牌，
 * 生成项目卡牌 JSON、下载中文卡图，并更新图片清单。
 *
 * 用法：
 *   node tools/import-tencent-doc-card-updates.mjs OP18 EB05
 */

import fs from "node:fs/promises";
import { createHash } from "node:crypto";
import { spawn } from "node:child_process";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { inflateSync } from "node:zlib";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, "..");
const DOCUMENT_ID = "DSkVsU1lkSnhPdmlk";
const DOCUMENT_URL = `https://docs.qq.com/sheet/${DOCUMENT_ID}`;
const USER_AGENT =
  "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124.0 Safari/537.36";

const SOURCES = {
  OP18: {
    tab: "sla67z",
    setName: "补充包【OPC-18】",
    subscript: 4,
  },
  EB05: {
    tab: "pk9w4p",
    setName: "特别补充包【EBC-05】",
    subscript: 4,
  },
};

const requestedSets = process.argv
  .slice(2)
  .map((value) => value.toUpperCase())
  .filter(Boolean);
const setCodes = requestedSets.length ? requestedSets : Object.keys(SOURCES);
for (const setCode of setCodes) {
  if (!SOURCES[setCode]) throw new Error(`不支持的卡集：${setCode}`);
}

function readVarint(buffer, offset) {
  let value = 0;
  let shift = 0;
  let cursor = offset;
  while (cursor < buffer.length) {
    const byte = buffer[cursor++];
    value += (byte & 0x7f) * 2 ** shift;
    if ((byte & 0x80) === 0) return [value, cursor];
    shift += 7;
    if (shift > 53) throw new Error("protobuf varint 超出安全整数范围");
  }
  throw new Error("protobuf varint 意外结束");
}

function parseMessage(buffer) {
  const fields = [];
  let offset = 0;
  while (offset < buffer.length) {
    let tag;
    [tag, offset] = readVarint(buffer, offset);
    const field = Math.floor(tag / 8);
    const wire = tag % 8;
    let value;
    if (wire === 0) {
      [value, offset] = readVarint(buffer, offset);
    } else if (wire === 1) {
      value = buffer.subarray(offset, offset + 8);
      offset += 8;
    } else if (wire === 2) {
      let length;
      [length, offset] = readVarint(buffer, offset);
      value = buffer.subarray(offset, offset + length);
      offset += length;
    } else if (wire === 5) {
      value = buffer.subarray(offset, offset + 4);
      offset += 4;
    } else {
      throw new Error(`暂不支持 protobuf wire type ${wire}`);
    }
    if (offset > buffer.length) throw new Error("protobuf 字段长度越界");
    fields.push({ field, wire, value });
  }
  return fields;
}

function fieldsOf(message, field) {
  return message.filter((item) => item.field === field);
}

function child(field) {
  return parseMessage(field.value);
}

function directText(field) {
  if (!field) return "";
  const textField = fieldsOf(child(field), 1)[0];
  return textField?.wire === 2 ? textField.value.toString("utf8") : "";
}

function richText(field) {
  let result = "";
  for (const runField of fieldsOf(child(field), 3)) {
    const contentField = fieldsOf(child(runField), 3)[0];
    if (!contentField) continue;
    const textField = fieldsOf(child(contentField), 1)[0];
    if (textField?.wire === 2) result += textField.value.toString("utf8");
  }
  return result;
}

function decodeSheet(buffer, setCode) {
  const top = child(fieldsOf(parseMessage(buffer), 1)[0]);
  const sheetSection = fieldsOf(top, 5).find((field) => {
    try {
      return fieldsOf(child(field), 19).length > 0;
    } catch {
      return false;
    }
  });
  if (!sheetSection) throw new Error(`${setCode} 未找到工作表数据区`);

  const sheet = child(fieldsOf(child(sheetSection), 19)[0]);
  const shared = child(fieldsOf(sheet, 5)[0]);
  const plainValues = fieldsOf(shared, 1).map(directText);
  const richValues = fieldsOf(shared, 2).map(richText);
  const numericValues = fieldsOf(shared, 3).map((field) => {
    const numberField = fieldsOf(child(field), 1)[0];
    return numberField.value.readDoubleLE(0);
  });

  const rows = new Map();
  for (const cellField of fieldsOf(sheet, 6)) {
    const cell = child(cellField);
    const row = fieldsOf(cell, 1)[0]?.value ?? 0;
    const column = fieldsOf(cell, 2)[0]?.value ?? 0;
    const data = child(fieldsOf(cell, 3)[0]);
    const valueType = fieldsOf(data, 1)[0]?.value;
    const valueReference = fieldsOf(data, 2)[0];
    let value = "";
    if (valueReference) {
      const reference = fieldsOf(child(valueReference), 1)[0]?.value;
      if (valueType === 4) value = plainValues[reference] ?? "";
      if (valueType === 6) value = richValues[reference] ?? "";
      if (valueType === 2) {
        const number = reference >= 129
          ? numericValues[reference - 129]
          : reference;
        value = Number.isFinite(number) ? String(number) : "";
      }
    }

    let imageUrl = "";
    const payloadField = fieldsOf(data, 17)[0];
    if (payloadField) {
      const payload = directText(payloadField);
      imageUrl = payload.match(/https:\/\/[^"\]]+/)?.[0] ?? "";
    }

    if (!rows.has(row)) rows.set(row, new Map());
    rows.get(row).set(column, { value: value.trim(), imageUrl });
  }

  const valueAt = (columns, index) => columns.get(index)?.value ?? "";
  const imageAt = (columns, index) => columns.get(index)?.imageUrl ?? "";
  const cards = [];
  for (const [row, columns] of rows) {
    const number = valueAt(columns, 4).toUpperCase();
    const name = valueAt(columns, 6);
    if (!new RegExp(`^${setCode}-\\d{3}$`).test(number) || !name) continue;
    cards.push({
      row: row + 1,
      number,
      rarity: valueAt(columns, 5),
      name,
      color: valueAt(columns, 7),
      type: valueAt(columns, 8),
      property: valueAt(columns, 9),
      keyWords: valueAt(columns, 10),
      power: valueAt(columns, 11),
      cost: valueAt(columns, 12),
      counter: valueAt(columns, 13),
      effectText: valueAt(columns, 14),
      trigger: valueAt(columns, 15),
      originalImageUrl: imageAt(columns, 1),
      chineseImageUrl: imageAt(columns, 2),
      alternateImageUrl: imageAt(columns, 3),
    });
  }
  return cards.sort((a, b) => a.number.localeCompare(b.number, "en", { numeric: true }));
}

function normalizeCard(card, setCode) {
  const source = SOURCES[setCode];
  const typeMap = {
    LEADER: "领航",
    CHARACTER: "角色",
    EVENT: "事件",
    STAGE: "舞台",
  };
  const type = card.rarity === "L" ? "领航" : (typeMap[card.type] ?? card.type);
  const counterNumber = card.counter.match(/\d+/)?.[0] ?? "";
  const trigger = card.trigger && !card.trigger.startsWith("【触发】")
    ? `【触发】${card.trigger}`
    : card.trigger;
  return {
    number: card.number,
    name: card.name,
    color: card.color.replaceAll("・", "/"),
    type,
    property: card.property.replaceAll("斬", "斩").replaceAll("？", "?"),
    power: card.power === "-" ? "" : card.power,
    cost: card.cost === "-" ? "" : card.cost,
    keyWords: card.keyWords.replace(/[・,，]/g, "/"),
    counter: counterNumber ? `反击+${counterNumber}` : "",
    effectText: card.effectText,
    effectEvent: "",
    rarity: card.rarity,
    subscript: source.subscript,
    trigger,
    set: source.setName,
    image: `/cards/${setCode.toLowerCase()}/${card.number}.png`,
    cartograph: "",
  };
}

async function fetchText(url) {
  const response = await fetch(url, {
    headers: { Referer: DOCUMENT_URL, "User-Agent": USER_AGENT },
  });
  if (!response.ok) throw new Error(`请求失败 HTTP ${response.status}：${url}`);
  return response.text();
}

async function loadSheetBuffer(tab, setCode) {
  const html = await fetchText(`${DOCUMENT_URL}?tab=${tab}`);
  const source = html.match(/<script[^>]+id="opendoc-jsonp"[^>]+src="([^"]+)/)?.[1];
  if (!source) throw new Error(`${setCode} 页面未找到 opendoc 接口`);
  const apiUrl = `https:${source.replaceAll("&amp;", "&")}`.replace("&&", "&");
  const jsonp = await fetchText(apiUrl);
  const payload = JSON.parse(jsonp.slice(jsonp.indexOf("(") + 1, jsonp.lastIndexOf(")")));
  const collab = payload.clientVars?.collab_client_vars;
  if (collab?.padSubId !== tab) {
    throw new Error(`${setCode} 工作表校验失败：期望 ${tab}，实际 ${collab?.padSubId}`);
  }
  const initial = collab.initialAttributedText?.text?.[0];
  const block = initial?.block_datas?.[0];
  if (!block?.related_sheet) throw new Error(`${setCode} 工作表没有可解码的数据块`);
  return inflateSync(Buffer.from(block.related_sheet, "base64"));
}

async function loadSharp() {
  const modulePath = path.join(
    ROOT,
    "opcgpro-web",
    "node_modules",
    "sharp",
    "lib",
    "index.js",
  );
  try {
    return (await import(pathToFileURL(modulePath).href)).default;
  } catch (error) {
    throw new Error(`无法加载图片处理依赖 sharp，请先安装前端依赖：${error.message}`);
  }
}

async function fetchImage(url) {
  const response = await fetch(url, {
    headers: { Referer: DOCUMENT_URL, "User-Agent": USER_AGENT },
  });
  if (!response.ok) throw new Error(`卡图下载失败 HTTP ${response.status}：${url}`);
  const buffer = Buffer.from(await response.arrayBuffer());
  if (buffer.length < 1024) throw new Error(`卡图内容异常，仅 ${buffer.length} 字节：${url}`);
  return buffer;
}

async function writeCardImages(cards, setCode, sharp) {
  const outputDir = path.join(ROOT, "CardImages", setCode.toLowerCase());
  await fs.mkdir(outputDir, { recursive: true });
  const written = new Map();
  for (const card of cards) {
    const mainUrl = card.chineseImageUrl || card.originalImageUrl;
    if (!mainUrl) throw new Error(`${card.number} 缺少卡图`);
    const tasks = [{ url: mainUrl, filename: `${card.number}.png` }];
    if (card.alternateImageUrl && card.alternateImageUrl !== mainUrl) {
      tasks.push({ url: card.alternateImageUrl, filename: `${card.number}_01.png` });
    }
    const sprites = [];
    for (const task of tasks) {
      const buffer = await fetchImage(task.url);
      const outputPath = path.join(outputDir, task.filename);
      await sharp(buffer)
        .resize({ width: 868, withoutEnlargement: true })
        .png({ compressionLevel: 9, effort: 10 })
        .toFile(outputPath);
      const digest = createHash("sha256")
        .update(await fs.readFile(outputPath))
        .digest("hex")
        .slice(0, 12);
      sprites.push(`/cards/${setCode.toLowerCase()}/${task.filename}?v=${digest}`);
    }
    written.set(card.number, sprites);
    console.log(`  卡图 ${card.number}：${sprites.length} 张`);
  }
  return written;
}

function replaceManifestGroup(manifest, setCode, spritesByCard) {
  const currentEntries = Object.entries(manifest)
    .filter(([number]) => !number.startsWith(`${setCode}-`));
  const setEntries = [...spritesByCard.entries()]
    .sort(([a], [b]) => a.localeCompare(b, "en", { numeric: true }));
  const family = setCode.match(/^[A-Z]+/)?.[0] ?? setCode;
  const lastFamilyIndex = currentEntries.findLastIndex(([number]) =>
    number.startsWith(family) && /^\d/.test(number.slice(family.length)),
  );
  currentEntries.splice(lastFamilyIndex + 1, 0, ...setEntries);
  return Object.fromEntries(currentEntries);
}

async function writeJsonCopies(cards, setCode) {
  const json = `${JSON.stringify(cards, null, 2)}\n`;
  const targets = [
    path.join(ROOT, "卡牌数据_含原文", `${setCode}.json`),
    path.join(ROOT, "卡牌数据", `${setCode}.json`),
    path.join(ROOT, "opcgpro-web", "public", "data", `${setCode}.json`),
  ];
  await Promise.all(targets.map((target) => fs.writeFile(target, json, "utf8")));
}

async function main() {
  const sharp = await loadSharp();
  const manifestPath = path.join(ROOT, "opcgpro-web", "public", "data", "imageManifest.json");
  let manifest = JSON.parse(await fs.readFile(manifestPath, "utf8"));
  for (const setCode of setCodes) {
    console.log(`读取 ${setCode}……`);
    const buffer = await loadSheetBuffer(SOURCES[setCode].tab, setCode);
    const sourceCards = decodeSheet(buffer, setCode);
    if (!sourceCards.length) throw new Error(`${setCode} 没有已填写的卡牌`);
    const cards = sourceCards.map((card) => normalizeCard(card, setCode));
    await writeJsonCopies(cards, setCode);
    const sprites = await writeCardImages(sourceCards, setCode, sharp);
    manifest = replaceManifestGroup(manifest, setCode, sprites);
    console.log(`  数据 ${setCode}：${cards.length} 张`);
  }
  await fs.writeFile(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
}

await main();

await new Promise((resolve, reject) => {
  const migration = spawn(
    process.execPath,
    [path.join(ROOT, "tools", "strip-effecttext.mjs"), "--write", ...setCodes],
    { cwd: ROOT, stdio: "inherit" },
  );
  migration.once("error", reject);
  migration.once("exit", (code) => {
    if (code === 0) resolve();
    else reject(new Error(`结构化卡牌效果失败，退出码 ${code}`));
  });
});
