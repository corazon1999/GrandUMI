import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { normalizeCardColor } from "../../tools/card-color-normalizer.mjs";
import { decodeSheet, normalizeCard, mergeTencentCardUpdates } from "../../tools/import-tencent-doc-card-updates.mjs";

const testsDir = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(testsDir, "..");
const repoRoot = path.resolve(webRoot, "..");

const EXPECTED_NUMBERS = {
  OP18: "001 003 011 016 017 021 022 024 025 028 031 034 041 044 046 048 055 056 060 061 065 066 069 076 078 079 084 086 089 091 093 100 106 112 113 119"
    .split(" ").map((suffix) => `OP18-${suffix}`),
  EB05: Array.from({length: 61}, (_, index) => `EB05-${String(index + 1).padStart(3, "0")}`),
};

const EXPECTED_COLORS = {
  "OP18-021": "绿/紫",
  "OP18-060": "紫/黑",
  "OP18-080": "紫/黑",
  "EB05-010": "绿/黄",
};

test("罗宾编号纠正保留其旧扩展资料，但不能继承为斯皮德别名或能力，重复导入保持一致", () => {
  const old = [{number: "EB05-035", name: "妮古·罗宾", customDetail: "保留资料", abilities: ["旧罗宾能力"], alsoNames: ["罗宾"]}];
  const incoming = [{number: "EB05-033", name: "妮古·罗宾", image: "/cards/eb05/EB05-033.png"},
    {number: "EB05-035", name: "斯皮德", abilities: [], image: "/cards/eb05/EB05-035.png"}];
  const merged = mergeTencentCardUpdates(old, incoming);
  const robin = merged.find(card => card.number === "EB05-033");
  const speed = merged.find(card => card.number === "EB05-035");
  assert.equal(robin.customDetail, "保留资料");
  assert.deepEqual(robin.alsoNames, ["罗宾"]);
  assert.equal(speed.customDetail, undefined);
  assert.equal(speed.alsoNames, undefined);
  assert.deepEqual(speed.abilities, []);
  assert.deepEqual(mergeTencentCardUpdates(merged, incoming), merged);
});

test("十月九日新卡与纠正卡的角标属性及罗宾斯皮德身份符合中文卡面", async () => {
  const all = (await Promise.all(["OP18", "EB05"].map(set => readFile(path.join(repoRoot, "卡牌数据", `${set}.json`), "utf8").then(JSON.parse)))).flat();
  const changed = "OP18-017 OP18-055 OP18-069 OP18-089 OP18-091 OP18-106 EB05-003 EB05-008 EB05-015 EB05-019 EB05-026 EB05-032 EB05-033 EB05-035 EB05-040 EB05-041 EB05-049 EB05-058 EB05-059".split(" ");
  for (const number of changed) assert.equal(all.find(card => card.number === number).subscript, 5, number);
  assert.equal(all.find(card => card.number === "OP18-055").property, "斩/打");
  const robin = all.find(card => card.number === "EB05-033");
  const speed = all.find(card => card.number === "EB05-035");
  assert.equal(robin.name, "妮古·罗宾");
  assert.equal(robin.cost, "1");
  assert.equal(robin.counter, "反击+2000");
  assert.equal(speed.name, "斯皮德");
  assert.equal(speed.cost, "6");
  assert.equal(speed.power, "6000");
  assert.ok(!(speed.alsoNames ?? []).some(name => name.includes("罗宾")));
  for (const number of ["EB05-008", "EB05-019", "EB05-040", "EB05-049", "EB05-059"])
    assert.equal(all.find(card => card.number === number).property, "");
});

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
