import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { decodeSheet, normalizeCard } from "../../tools/import-tencent-doc-card-updates.mjs";

const readJson = async (relative) => JSON.parse(await readFile(new URL(relative, import.meta.url), "utf8"));

test("新预组共用表识别左移后的资料列并排除空白预留卡号", () => {
  const varint = (number) => {
    const bytes = [];
    do { bytes.push((number & 127) | (number > 127 ? 128 : 0)); number >>>= 7; } while (number);
    return Buffer.from(bytes);
  };
  const value = (field, number) => Buffer.concat([varint(field * 8), varint(number)]);
  const block = (field, data) => Buffer.concat([varint(field * 8 + 2), varint(data.length), data]);
  const strings = ["ST37-001", "L", "蒙奇·D·路飞", "红,蓝", "LEADER", "打", "阿拉巴斯坦王国/草帽一伙", "5000", "3", "-", "【攻击时】抽取2张卡牌", "ST38-002"];
  const shared = Buffer.concat(strings.map(text => block(1, block(1, Buffer.from(text)))));
  const cell = (row, column, index) => block(6, Buffer.concat([
    value(1, row), value(2, column), block(3, Buffer.concat([value(1, 4), block(2, value(1, index))])),
  ]));
  const sheet = Buffer.concat([block(5, shared), ...strings.slice(0, 11).map((_, index) => cell(3, index + 3, index)), cell(4, 3, 11)]);
  const buffer = block(1, block(5, block(19, sheet)));
  const [card] = decodeSheet(buffer, "ST37");
  assert.equal(card.number, "ST37-001");
  assert.equal(card.name, "蒙奇·D·路飞");
  assert.equal(card.color, "红,蓝");
  assert.equal(card.cost, "3");
  assert.equal(card.alternateImageUrl, "");
  assert.deepEqual(decodeSheet(buffer, "ST38"), []);
  const normalized = normalizeCard(card, "ST37");
  assert.equal(normalized.color, "红/蓝");
  assert.equal(normalized.subscript, 5);
});

test("两张新领航前后端资料一致且卡面数值正确", async () => {
  for (const [set, color, life] of [["ST37", "红/蓝", "3"], ["ST38", "蓝/黑", "4"]]) {
    const server = await readJson(`../../卡牌数据/${set}.json`);
    const client = await readJson(`../public/data/${set}.json`);
    assert.deepEqual(client, server);
    assert.equal(server.length, 1);
    const [card] = server;
    assert.equal(card.number, `${set}-001`);
    assert.equal(card.color, color);
    assert.equal(card.cost, life);
    assert.equal(card.power, "5000");
    assert.equal(card.subscript, 5);
    assert.equal(card.type, "领航");
    assert.ok(card.effectTags.includes("OnAttackDeclare"));
    assert.equal(Object.hasOwn(card, "effectText"), false);
  }
});

test("新领航中文卡图清单有内容版本且前端单包包含新卡", async () => {
  const manifest = await readJson("../public/data/imageManifest.json");
  const bundle = await readJson("../public/data/allCards.json");
  for (const set of ["ST37", "ST38"]) {
    const number = `${set}-001`;
    assert.equal(manifest[number].length, 1);
    assert.match(manifest[number][0], new RegExp(`^/cards/${set.toLowerCase()}/${number}\\.png\\?v=[a-f0-9]{12}$`));
    assert.ok(bundle.cards.some(card => card.number === number), number);
  }
});
