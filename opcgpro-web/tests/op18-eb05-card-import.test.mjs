import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { normalizeCardColor } from "../../tools/card-color-normalizer.mjs";
import { decodeSheet, normalizeCard } from "../../tools/import-tencent-doc-card-updates.mjs";

const testsDir = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(testsDir, "..");
const repoRoot = path.resolve(webRoot, "..");

const EXPECTED_NUMBERS = {
  OP18: "001 003 011 016 021 022 024 025 028 031 034 041 044 046 048 056 060 061 065 066 076 078 079 084 086 093 100 112 113 119"
    .split(" ").map((suffix) => `OP18-${suffix}`),
  EB05: "001 002 004 005 006 007 009 010 011 012 013 014 016 017 018 020 021 022 023 024 025 027 028 029 030 031 034 035 036 037 038 039 042 043 044 045 046 047 048 050 051 052 053 054 055 056 057 060 061"
    .split(" ").map((suffix) => `EB05-${suffix}`),
};

const EXPECTED_COLORS = {
  "OP18-021": "绿/紫",
  "OP18-060": "紫/黑",
  "OP18-080": "紫/黑",
  "EB05-010": "绿/黄",
};

test("OP18 与 EB05 已公开卡牌同步到服务端和客户端数据源", async () => {
  for (const [setCode, expectedNumbers] of Object.entries(EXPECTED_NUMBERS)) {
    const paths = [
      path.join(repoRoot, "卡牌数据", `${setCode}.json`),
      path.join(webRoot, "public", "data", `${setCode}.json`),
    ];
    const [server, client] = await Promise.all(
      paths.map((file) => readFile(file, "utf8").then(JSON.parse)),
    );

    assert.deepEqual(server.map((card) => card.number), expectedNumbers);
    assert.deepEqual(client, server);
    assert.ok(server.every((card) => !Object.hasOwn(card, "effectText")));
    assert.ok(server.every((card) => Array.isArray(card.effectTags)));
  }
});

test("腾讯 protobuf 默认零值仍能识别卡号共享表第零项及零力量", () => {
  const varint = (number) => {
    const bytes = [];
    do { bytes.push((number & 127) | (number > 127 ? 128 : 0)); number >>>= 7; } while (number);
    return Buffer.from(bytes);
  };
  const value = (field, number) => Buffer.concat([varint(field * 8), varint(number)]);
  const block = (field, content) => Buffer.concat([varint(field * 8 + 2), varint(content.length), content]);
  const text = (content) => block(1, block(1, Buffer.from(content)));
  const shared = Buffer.concat([text("OP18-011"), text("奈菲特·薇薇"), text("CHARACTER")]);
  const cell = (column, type, reference) => block(6, Buffer.concat([
    value(1, 3), value(2, column), block(3, Buffer.concat([
      value(1, type), block(2, reference === 0 ? Buffer.alloc(0) : value(1, reference)),
    ])),
  ]));
  const sheet = Buffer.concat([block(5, shared), cell(4, 4, 0), cell(6, 4, 1), cell(8, 4, 2), cell(11, 2, 0)]);
  const protobuf = block(1, block(5, block(19, sheet)));
  const cards = decodeSheet(protobuf, "OP18");
  assert.equal(cards.length, 1);
  assert.equal(cards[0].number, "OP18-011");
  assert.equal(cards[0].power, "0");
});

test("中文卡面校正不会被下次导入的错误类型颜色或空白值覆盖", () => {
  const source = {number: "EB05-030", name: "大家要保重喔♡", color: "紫", type: "CHARACTER",
    property: "事件卡", power: "", cost: "2", counter: "", keyWords: "东海/草帽一伙", trigger: "", rarity: "R"};
  const event = normalizeCard(source, "EB05");
  assert.equal(event.type, "事件");
  assert.equal(event.color, "蓝");
  assert.equal(event.property, "");
  assert.equal(event.subscript, 5);
  const iceburg = normalizeCard({...source, number: "OP18-061", power: "", counter: ""}, "OP18");
  assert.equal(iceburg.power, "0");
  assert.equal(iceburg.counter, "反击+1000");
});

test("十月新增卡的角标零力量及奈美反击值与中文卡面一致", async () => {
  const op18 = JSON.parse(await readFile(path.join(repoRoot, "卡牌数据", "OP18.json"), "utf8"));
  const eb05 = JSON.parse(await readFile(path.join(repoRoot, "卡牌数据", "EB05.json"), "utf8"));
  const suffixes = new Set("011 024 034 046 048 061 084 093 100 113".split(" "));
  assert.ok(op18.filter(card => suffixes.has(card.number.slice(-3))).every(card => card.subscript === 5));
  for (const number of ["OP18-011", "OP18-024", "OP18-061"]) {
    assert.equal(op18.find(card => card.number === number).power, "0");
  }
  assert.equal(op18.find(card => card.number === "OP18-061").counter, "反击+1000");
  assert.equal(op18.find(card => card.number === "OP18-046").rarity, "SR");
  assert.equal(eb05.find(card => card.number === "EB05-030").type, "事件");
  assert.equal(eb05.find(card => card.number === "EB05-030").subscript, 5);
  assert.equal(eb05.find(card => card.number === "EB05-061").counter, "反击+2000");
});

test("OP18 与 EB05 卡图均使用本地资源并带内容版本键", async () => {
  const manifest = JSON.parse(
    await readFile(path.join(webRoot, "public", "data", "imageManifest.json"), "utf8"),
  );

  for (const [setCode, expectedNumbers] of Object.entries(EXPECTED_NUMBERS)) {
    for (const number of expectedNumbers) {
      const sprites = manifest[number];
      assert.ok(sprites?.length >= 1, `${number} 缺少图片清单`);
      for (const sprite of sprites) {
        assert.match(
          sprite,
          new RegExp(`^/cards/${setCode.toLowerCase()}/${number}(?:_\\d{2})?\\.png\\?v=[a-f0-9]{12}$`),
        );
      }
    }
  }
});

test("OP18 与 EB05 已公开领袖使用正确的双色数据", async () => {
  const paths = [
    path.join(repoRoot, "卡牌数据", "OP18.json"),
    path.join(webRoot, "public", "data", "OP18.json"),
    path.join(repoRoot, "卡牌数据", "EB05.json"),
    path.join(webRoot, "public", "data", "EB05.json"),
  ];

  for (const file of paths) {
    const cards = JSON.parse(await readFile(file, "utf8"));
    for (const card of cards) {
      const expected = EXPECTED_COLORS[card.number];
      if (expected) assert.equal(card.color, expected, `${card.number} 颜色错误：${file}`);
    }
  }
});

test("腾讯文档后续导入会规范化并修正三张卡的颜色", () => {
  assert.equal(normalizeCardColor("OP18-021", "绿"), EXPECTED_COLORS["OP18-021"]);
  assert.equal(normalizeCardColor("OP18-080", "黑"), EXPECTED_COLORS["OP18-080"]);
  assert.equal(normalizeCardColor("EB05-010", "绿"), EXPECTED_COLORS["EB05-010"]);
  assert.equal(normalizeCardColor("SAMPLE-001", "红,蓝"), "红/蓝");
});

test("卡组搜索会加载 OP18 与 EB05", async () => {
  const cardSets = await readFile(path.join(webRoot, "src", "data", "cardSets.ts"), "utf8");
  assert.match(cardSets, /OP18: "\/data\/OP18\.json"/);
  assert.match(cardSets, /EB05: "\/data\/EB05\.json"/);
  assert.match(cardSets, /"OP18"/);
  assert.match(cardSets, /"EB05"/);
});
