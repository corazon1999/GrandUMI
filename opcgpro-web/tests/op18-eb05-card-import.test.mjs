import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { normalizeCardColor } from "../../tools/card-color-normalizer.mjs";

const testsDir = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(testsDir, "..");
const repoRoot = path.resolve(webRoot, "..");

const EXPECTED_NUMBERS = {
  OP18: ["OP18-021", "OP18-031", "OP18-060", "OP18-065", "OP18-078", "OP18-119"],
  EB05: ["EB05-010", "EB05-016"],
};

const EXPECTED_COLORS = {
  "OP18-021": "绿/紫",
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
