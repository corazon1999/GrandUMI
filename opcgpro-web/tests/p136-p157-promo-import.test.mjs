import assert from "node:assert/strict";
import { existsSync, readFileSync, statSync } from "node:fs";
import { readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const testsDir = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(testsDir, "..");
const repoRoot = path.resolve(webRoot, "..");
const loadJson = file => JSON.parse(readFileSync(file, "utf8"));
const physicalAssetTest = process.env.GRANDUMI_REPOSITORY_VERIFICATION === "1"
  ? { skip: "仓库验证只校验清单与可恢复链；测试服部署负责实体卡图完整性" }
  : {};

const expected = [
  ["P-136", "撒谎布", "红", "角色", "射", "2000", "1", "草帽一伙", "2000"],
  ["P-137", "山智", "红", "角色", "打", "7000", "6", "草帽一伙", "1000"],
  ["P-138", "托尼托尼·乔巴", "红", "角色", "打", "4000", "3", "动物/草帽一伙", "1000"],
  ["P-139", "奈美", "红", "角色", "知", "6000", "5", "草帽一伙", "1000"],
  ["P-140", "蒙奇·D·路飞", "红", "角色", "打", "8000", "7", "草帽一伙", ""],
  ["P-141", "罗罗诺亚·佐罗", "红", "角色", "斩", "6000", "7", "草帽一伙", "1000"],
  ["P-142", "前进·梅利号", "红", "舞台", "-", "", "1", "草帽一伙", ""],
  ["P-143", "克洛克达尔", "黑", "角色", "特", "8000", "6", "巴洛克工作室", ""],
  ["P-144", "Miss.全周日", "黑", "角色", "打", "6000", "5", "巴洛克工作室", "1000"],
  ["P-145", "Miss.星期三", "黑", "角色", "斩", "5000", "4", "巴洛克工作室", "1000"],
  ["P-146", "Miss.黄金周(玛丽安奴)", "黑", "角色", "知", "2000", "1", "巴洛克工作室", "1000"],
  ["P-147", "Miss.情人节(美琪塔)", "黑", "角色", "打", "3000", "3", "巴洛克工作室", "1000"],
  ["P-148", "Mr.3(加尔迪诺)", "黑", "角色", "特", "6000", "5", "巴洛克工作室", "1000"],
  ["P-149", "Mr.5(杰姆)", "黑", "角色", "特", "6000", "5", "巴洛克工作室", "1000"],
  ["P-150", "库赞", "黄", "角色", "特", "6000", "5", "黑胡子海盗团/原海军", "1000"],
  ["P-151", "斯摩格", "紫", "角色", "特", "6000", "5", "海军", "1000"],
  ["P-157", "蒙奇·D·路飞", "黑", "角色", "打", "6000", "7", "埃鲁巴夫/四皇/草帽一伙", "1000"],
];

test("17张宣传卡的服务端与前端简中数据完全一致", () => {
  const server = loadJson(path.join(repoRoot, "卡牌数据", "P.json"));
  const client = loadJson(path.join(webRoot, "public", "data", "P.json"));
  assert.deepEqual(client, server);

  for (const values of expected) {
    const [number, name, color, type, property, power, cost, keyWords, counter] = values;
    const card = server.find(item => item.number === number);
    assert.ok(card, `${number} 缺失`);
    assert.deepEqual(
      [card.number, card.name, card.color, card.type, card.property, card.power,
        card.cost, card.keyWords, card.counter],
      [number, name, color, type, property, power, cost, keyWords, counter],
      `${number} 基础数据不一致`,
    );
    assert.equal(card.set, "宣传卡", `${number} 卡集错误`);
    assert.equal(card.rarity, "P", `${number} 稀有度错误`);
    assert.equal(card.subscript, 5, `${number} 环境角标错误`);
    assert.match(card.image, /^https:\/\/source\.windoent\.com\/OnePiecePc\/Picture\/.+\.png$/);
    assert.equal(Object.hasOwn(card, "effectText"), false);
    assert.ok(Array.isArray(card.effectTags));
    assert.ok(Array.isArray(card.abilities));
  }

  const p150 = server.find(card => card.number === "P-150");
  assert.equal(
    p150.trigger,
    "【触发】抽取1张卡牌，本回合中，对方最多1张费用不高于6的角色无法攻击。",
  );
});

test("17张宣传卡的官方正画均进入带摘要的本地图片清单", () => {
  const manifest = loadJson(path.join(webRoot, "public", "data", "imageManifest.json"));
  for (const [number] of expected) {
    assert.equal(manifest[number]?.length, 1, `${number} 应有且仅有一张官网正画`);
    assert.match(manifest[number][0], new RegExp(`^/cards/p/${number}\\.png\\?v=[a-f0-9]{12}$`));
  }
});

test("17张宣传卡的本地官网原图存在且非空", physicalAssetTest, () => {
  for (const [number] of expected) {
    const file = path.join(webRoot, "public", "cards", "p", `${number}.png`);
    assert.equal(existsSync(file), true, `${number} 原图不存在`);
    assert.ok(statSync(file).size > 1024, `${number} 原图内容异常`);
  }
});

test("测试服会从提交数据恢复缺失正画且不写正式服资源目录", async () => {
  const [deploy, recovery, importer] = await Promise.all([
    readFile(path.join(repoRoot, "ops", "server", "deploy-test.sh"), "utf8"),
    readFile(path.join(repoRoot, "tools", "ensure-card-images-from-data.mjs"), "utf8"),
    readFile(path.join(repoRoot, "tools", "import-missing-card-data.mjs"), "utf8"),
  ]);
  assert.match(deploy, /ensure-card-images-from-data\.mjs/);
  assert.match(deploy, /--output-root="\$public_cards_link"/);
  assert.match(deploy, /--only-missing/);
  assert.match(recovery, /expectedDigest/);
  assert.match(recovery, /官网图片摘要/);
  assert.match(recovery, /path\.relative\(OUTPUT_ROOT/);
  assert.match(importer, /https:\/\/webadmin\.windoent\.com\/front\/op-public/);
  assert.match(importer, /--numbers=/);
});
