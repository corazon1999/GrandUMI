import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { normalizeCardColor } from "../../tools/card-color-normalizer.mjs";
import { decodeSheet, normalizeCard, mergeTencentCardUpdates, withTencentImageFallback } from "../../tools/import-tencent-doc-card-updates.mjs";

const testsDir = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(testsDir, "..");
const repoRoot = path.resolve(webRoot, "..");

const EXPECTED_NUMBERS = {
  OP18: Array.from({ length: 119 }, (_, i) => `OP18-${String(i + 1).padStart(3, "0")}`),
  EB05: Array.from({length: 61}, (_, index) => `EB05-${String(index + 1).padStart(3, "0")}`),
};

test("十月三十日OP18共119张，校正旺奥加颜色与杰利资料，保留双属性和规则别名", async () => {
  const cards = JSON.parse(await readFile(path.join(repoRoot, "卡牌数据/OP18.json"), "utf8"));
  const get = suffix => cards.find(card => card.number === `OP18-${suffix}`);
  assert.equal(cards.length, 119);
  assert.ok(get("118"));
  assert.equal(get("080").color, "黑");
  assert.equal(get("080").type, "角色");
  assert.equal(get("027").property, "斩/射");
  assert.equal(get("050").property, "特/知");
  assert.equal(get("054").property, "打/射");
  assert.ok(get("045").alsoNames.includes("山智"));
  assert.deepEqual([get("102").cost, get("102").power, get("102").counter, get("102").keyWords], ["4", "6000", "反击+1000", "CP6"]);
  assert.equal(get("098").keyWords, "中将/海军");
  assert.equal(get("116").keyWords, "CP9");
  assert.ok(cards.every(card => card.cost !== "" && card.name.trim() !== ""));
  for (const suffix of ["019", "020", "038", "039", "040", "057", "058", "059", "074", "094", "095", "096", "097", "115", "116", "117"])
    assert.equal(get(suffix).property, "");
  for (const suffix of ["026", "054", "071"])
    for (const tag of ["PreKO", "OnAllyWillBeKOd", "OnAllyWillLeaveField"]) assert.ok(get(suffix).effectTags.includes(tag));
  assert.deepEqual(get("080").effectTags, ["OnKO", "OnHandDiscarded"]);
  for (const suffix of ["004", "006", "007", "081"]) assert.deepEqual(get(suffix).abilities, []);
  assert.deepEqual(get("098").abilities, ["不可阻挡"]);
  assert.deepEqual(get("104").abilities, ["流放"]);
});

test("最后一张路飞秘藏卡的资料、中文图与第5角标正确，重复导入保留角标", async () => {
  const cards = JSON.parse(await readFile(path.join(repoRoot, "卡牌数据/OP18.json"), "utf8"));
  const card = cards.find(item => item.number === "OP18-118");
  assert.deepEqual([card.name, card.color, card.type, card.property, card.cost, card.power, card.counter,
    card.keyWords, card.rarity, card.subscript],
    ["蒙奇·D·路飞", "绿", "角色", "打", "9", "10000", "", "七水之城/草帽一伙", "SEC", 5]);
  assert.deepEqual(card.effectTags, ["OnEnterField", "OnMyTurnEnd"]);
  assert.deepEqual(card.abilities, []);
  assert.equal(card.image, "/cards/op18/OP18-118.png");
  const manifest = JSON.parse(await readFile(path.join(webRoot, "public/data/imageManifest.json"), "utf8"));
  assert.equal(manifest[card.number].length, 1);
  assert.match(manifest[card.number][0], /^\/cards\/op18\/OP18-118\.png\?v=[0-9a-f]{12}$/);
  assert.equal(normalizeCard({ ...card, type: "CHARACTER", effectText: "" }, "OP18").subscript, 5);
});

test("新卡重复导入不恢复误填颜色、空数值与事件舞台属性", () => {
  const blank = { name: "测试", rarity: "C", type: "CHARACTER", color: "黄", property: "", power: "", cost: "", counter: "", keyWords: "CP9", trigger: "", effectText: "" };
  const jerry = normalizeCard({ ...blank, number: "OP18-102" }, "OP18");
  assert.deepEqual([jerry.cost, jerry.power, jerry.counter, jerry.keyWords, jerry.subscript], ["4", "6000", "反击+1000", "CP6", 5]);
  assert.equal(normalizeCard({ ...blank, number: "OP18-080", color: "紫/黑" }, "OP18").color, "黑");
  for (const type of ["EVENT", "STAGE"])
    assert.equal(normalizeCard({ ...blank, number: "OP18-117", type, property: "事件卡" }, "OP18").property, "");
});

test("十月二十七日七张新卡的基础能力、触发、双属性与补图角标正确", async () => {
  const cards = JSON.parse(await readFile(path.join(repoRoot, "卡牌数据/OP18.json"), "utf8"));
  const expected = {
    "013": ["5", "6000", "反击+1000", ["OnEnterField"], []],
    "018": ["1", "", "", ["EventMain"], []],
    "029": ["9", "6000", "反击+1000", ["OnAttackDeclare"], ["速攻"]],
    "051": ["2", "3000", "反击+1000", ["OnEnterField"], []],
    "064": ["5", "6000", "反击+1000", ["OnEnterField", "OnKO"], []],
    "070": ["1", "2000", "反击+2000", ["OnEnterField"], []],
    "103": ["6", "8000", "", ["OnKO"], []],
  };
  for (const [suffix, values] of Object.entries(expected)) {
    const card = cards.find(item => item.number === `OP18-${suffix}`);
    assert.deepEqual([card.cost, card.power, card.counter, card.effectTags, card.abilities], values, card.number);
    assert.equal(card.image, `/cards/op18/${card.number}.png`);
    assert.equal(card.subscript, 5);
    if (["018", "103"].includes(suffix)) assert.ok(card.trigger.startsWith("【触发】"));
  }
  assert.equal(cards.find(card => card.number === "OP18-051").property, "斩/打");
  assert.equal(cards.find(card => card.number === "OP18-018").property, "");
});

test("两张后补图的新卡重复导入保留真实图片路径与第5角标", () => {
  const blank = {name: "测试", rarity: "R", type: "CHARACTER", color: "黄", property: "打",
    power: "8000", cost: "6", counter: "-", keyWords: "CP9", trigger: "", effectText: ""};
  for (const number of ["OP18-018", "OP18-103"]) {
    for (const images of [{}, {chineseImageUrl: "https://example.com/中文图.png"}]) {
      const card = normalizeCard({...blank, number, ...images}, "OP18");
      assert.equal(card.image, `/cards/op18/${number}.png`);
      assert.equal(card.subscript, 5);
    }
  }
});

test("十月二十三日两张新卡保留零力量和第5角标，持续获得的速攻不误写为基础能力", async () => {
  const cards = JSON.parse(await readFile(path.join(repoRoot, "卡牌数据", "OP18.json"), "utf8"));
  const zoro = cards.find(card => card.number === "OP18-092");
  const funkfreed = cards.find(card => card.number === "OP18-108");
  assert.equal(zoro.name, "罗罗诺亚·佐罗");
  assert.equal(zoro.color, "黑");
  assert.equal(zoro.power, "2000");
  assert.equal(zoro.cost, "2");
  assert.deepEqual(zoro.effectTags, []);
  assert.deepEqual(funkfreed.effectTags, ["OnEnterField", "ActivatedMain"]);
  assert.equal(funkfreed.name, "范克弗里德");
  assert.equal(funkfreed.color, "黄");
  assert.equal(funkfreed.power, "0");
  assert.equal(funkfreed.cost, "1");
  for (const card of [zoro, funkfreed]) {
    assert.equal(card.subscript, 5);
    assert.equal(card.counter, "反击+1000");
    assert.equal(card.property, "斩");
    assert.deepEqual(card.abilities, []);
  }
  const manifest = JSON.parse(await readFile(path.join(webRoot, "public/data/imageManifest.json"), "utf8"));
  for (const card of [zoro, funkfreed]) {
    assert.equal(manifest[card.number].length, 1);
    assert.match(manifest[card.number][0], new RegExp(`/cards/op18/${card.number}\\.png\\?v=[0-9a-f]{12}$`));
  }
});

test("两张新卡重复导入保留卡面校正，范克弗里德名称去除表格不可见字符", () => {
  const blank = { rarity: "UC", type: "CHARACTER", color: "黄", property: "斬", name: "\u200c范克弗里德\u200c",
    power: "", cost: "1", counter: "1000", keyWords: "CP9", trigger: "", effectText: "" };
  const card = normalizeCard({ ...blank, number: "OP18-108" }, "OP18");
  assert.equal(card.name, "范克弗里德");
  assert.equal(card.power, "0");
  assert.equal(card.subscript, 5);
  assert.equal(normalizeCard({ ...blank, number: "OP18-092" }, "OP18").subscript, 5);
});

test("十月二十日五张新卡资料与触发连线正确，获得型能力不误作基础能力", async () => {
  const cards = JSON.parse(await readFile(path.join(repoRoot, "卡牌数据", "OP18.json"), "utf8"));
  const expectedTags = {
    "OP18-015": ["OnOppAttackDeclare"], "OP18-036": ["OnMyTurnEnd"], "OP18-047": ["OnEnterField"],
    "OP18-075": ["OnEnterField", "ActivatedMain"], "OP18-077": ["OnEnterField", "ActivatedMain"],
  };
  for (const [number, tags] of Object.entries(expectedTags)) {
    const card = cards.find(item => item.number === number);
    assert.equal(card.subscript, 5, number);
    assert.deepEqual(card.effectTags, tags, number);
    assert.deepEqual(card.abilities, [], number);
    assert.equal(card.counter, ["OP18-075", "OP18-077"].includes(number) ? "" : "反击+2000");
  }
  for (const number of ["OP18-015", "OP18-036"]) assert.equal(cards.find(card => card.number === number).power, "0");
  for (const number of ["OP18-075", "OP18-077"]) {
    const card = cards.find(card => card.number === number);
    assert.equal(card.type, "舞台");
    assert.equal(card.property, "");
    assert.equal(card.cost, "5");
  }
});

test("十月二十日仅新增五张正画并替换两张中文图，保留已有异画", async () => {
  const manifest = JSON.parse(await readFile(path.join(webRoot, "public/data/imageManifest.json"), "utf8"));
  const expected = {"OP18-044": "4703052d57c3", "OP18-085": "ddc18db26815"};
  for (const [number, digest] of Object.entries(expected))
    assert.equal(manifest[number][0], `/cards/op18/${number}.png?v=${digest}`);
  for (const suffix of ["015", "036", "047", "075", "077"])
    assert.equal(manifest[`OP18-${suffix}`].length, 1);
  assert.equal(manifest["OP18-031"].length, 2);
  assert.equal(manifest["EB05-014"].length, 2);
  for (const suffix of ["001", "004", "006", "018", "021", "028", "034", "037", "043", "046", "052", "057", "061"])
    assert.equal(manifest[`EB05-${suffix}`].length, 1);
});

test("十月十五日新卡角标属性基础能力与名称更新符合已核对卡面", async () => {
  const cards = JSON.parse(await readFile(path.join(repoRoot, "卡牌数据", "OP18.json"), "utf8"));
  const get = number => cards.find(card => card.number === number);
  for (const suffix of ["010", "032", "052", "063", "085", "101", "111"]) {
    const card = get(`OP18-${suffix}`);
    assert.equal(card.subscript, 5, card.number);
    assert.equal(card.counter, ["101"].includes(suffix) ? "" : suffix === "032" ? "反击+1000" : "反击+2000");
  }
  assert.equal(get("OP18-052").property, "打/特");
  assert.equal(get("OP18-085").power, "0");
  assert.deepEqual(get("OP18-085").abilities, []);
  assert.deepEqual(get("OP18-111").abilities, ["速攻"]);
  assert.equal(get("OP18-119").name, "夏姆洛克圣");
  assert.ok(get("OP18-119").alsoNames.includes("夏姆洛克宫"));
});

test("刻耳贝洛斯缺图时使用经确认的日文卡面，后续中文图优先且不影响其它卡", () => {
  const blank = {number: "OP18-085", chineseImageUrl: "", originalImageUrl: ""};
  assert.match(withTencentImageFallback(blank).originalImageUrl, /^https:\/\/images\.tcg-portal\.jp\//);
  const chinese = {...blank, chineseImageUrl: "https://docimg1.docs.qq.com/image/中文图.png"};
  assert.equal(withTencentImageFallback(chinese), chinese);
  const japanese = {...blank, originalImageUrl: "https://docimg1.docs.qq.com/image/原图.png"};
  assert.equal(withTencentImageFallback(japanese), japanese);
  const other = {...blank, number: "OP18-010"};
  assert.equal(withTencentImageFallback(other), other);
});

const EXPECTED_COLORS = {
  "OP18-021": "绿/紫",
  "OP18-060": "紫/黑",
  "OP18-080": "黑",
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
