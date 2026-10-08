import assert from "node:assert/strict";
import { mkdir, mkdtemp, readFile, rm, unlink, writeFile } from "node:fs/promises";
import { spawnSync } from "node:child_process";
import path from "node:path";
import process from "node:process";
import test from "node:test";

const webRoot = path.resolve(import.meta.dirname, "..");
const repoRoot = path.resolve(webRoot, "..");
const tempRoot = process.env.GRANDUMI_TEST_TEMP_ROOT;
if (!tempRoot) throw new Error("卡牌可用状态测试必须设置 GRANDUMI_TEST_TEMP_ROOT。");

const expectedOp18 = "001 003 011 016 022 024 025 028 034 041 044 046 048 056 061 066 076 079 084 086 093 100 112 113"
  .split(" ").map((suffix) => `OP18-${suffix}`);
const oldEb05 = new Set(["EB05-010", "EB05-016"]);

test("OP18 与 EB05 的 71 张新卡完成效果后前后端均标记为可用", async () => {
  const [canonicalText, frontendText, eb05Text] = await Promise.all([
    readFile(path.join(repoRoot, "卡牌数据", "_playability.v1.json"), "utf8"),
    readFile(path.join(webRoot, "public", "data", "_playability.v1.json"), "utf8"),
    readFile(path.join(repoRoot, "卡牌数据", "EB05.json"), "utf8"),
  ]);
  assert.equal(frontendText, canonicalText, "前端状态镜像必须与服务端权威文件逐字节一致。 ");
  const playability = JSON.parse(canonicalText);
  const eb05 = JSON.parse(eb05Text);
  const expected = [
    ...expectedOp18,
    ...eb05.map((card) => card.number).filter((number) => !oldEb05.has(number)),
  ].sort();
  assert.equal(playability.schemaVersion, "grandumi.card-playability.v1");
  assert.equal(playability.pendingReason, "effect-implementation-pending");
  assert.equal(expected.length, 71);
  assert.deepEqual(playability.cards, []);
  for (const playable of [
    ...expected,
    "OP18-021", "OP18-031", "OP18-060", "OP18-065", "OP18-078", "OP18-119", "EB05-010", "EB05-016",
  ]) {
    assert.ok(!playability.cards.some((entry) => entry.number === playable), `${playable} 应可进入对局。`);
  }
});

test("资料页、卡组搜索与详情面板都明确标记待实现卡牌", async () => {
  const [catalog, search, details, store] = await Promise.all([
    readFile(path.join(webRoot, "src", "components", "home", "CardCatalogPanel.tsx"), "utf8"),
    readFile(path.join(webRoot, "src", "components", "deck-editor", "SearchResultPanel.tsx"), "utf8"),
    readFile(path.join(webRoot, "src", "components", "game", "CardInfoPanel.tsx"), "utf8"),
    readFile(path.join(webRoot, "src", "store", "deckStore.ts"), "utf8"),
  ]);
  assert.match(catalog, /data-card-playability="pending"/);
  assert.match(catalog, /效果开发中 · 暂不可对战/);
  assert.match(search, /data-card-playability="pending"/);
  assert.match(search, /暂不可对战/);
  assert.match(details, /资料与卡图可查阅，当前暂不可用于对战/);
  assert.match(store, /card\.playability === "pending"/);
  assert.match(store, /卡牌效果开发中，暂不可用于对战/);
});

test("卡牌单包只兼容状态文件缺失，存在但损坏或引用未知卡时失败关闭", async () => {
  const directory = await mkdtemp(path.join(tempRoot, "card-bundle-playability-"));
  try {
    const scripts = path.join(directory, "scripts");
    const data = path.join(directory, "public", "data");
    const sourceData = path.join(directory, "src", "data");
    await mkdir(scripts, { recursive: true });
    await mkdir(data, { recursive: true });
    await mkdir(sourceData, { recursive: true });
    await writeFile(
      path.join(scripts, "build-card-bundle.mjs"),
      await readFile(path.join(webRoot, "scripts", "build-card-bundle.mjs")),
    );
    await writeFile(path.join(data, "SAMPLE.json"), JSON.stringify([{ number: "OP01-001" }]), "utf8");
    await writeFile(path.join(data, "imageManifest.json"), "{}\n", "utf8");

    const run = () => spawnSync(process.execPath, [path.join(scripts, "build-card-bundle.mjs")], {
      cwd: directory,
      encoding: "utf8",
    });
    assert.equal(run().status, 0, "真正没有状态文件的旧卡表应继续兼容。 ");
    const legacyBundle = JSON.parse(await readFile(path.join(data, "allCards.json"), "utf8"));
    assert.deepEqual(legacyBundle.playability.cards, []);

    await writeFile(path.join(data, "_playability.v1.json"), "{损坏", "utf8");
    assert.notEqual(run().status, 0, "存在但损坏的状态文件不得降级为全卡可用。 ");

    await writeFile(path.join(data, "_playability.v1.json"), JSON.stringify({
      schemaVersion: "grandumi.card-playability.v1",
      cards: [{ number: "OP99-999", state: "pending" }],
    }), "utf8");
    assert.notEqual(run().status, 0, "状态文件引用未知卡时必须失败关闭。 ");

    await writeFile(path.join(data, "_playability.v1.json"), JSON.stringify({
      schemaVersion: "grandumi.card-playability.v1",
      cards: [{ number: "OP01-001", state: "pending" }],
    }), "utf8");
    assert.equal(run().status, 0, "独立夹具中的合法 pending 状态应保留。 ");
    const pendingBundle = JSON.parse(await readFile(path.join(data, "allCards.json"), "utf8"));
    assert.deepEqual(pendingBundle.playability.cards, [{ number: "OP01-001", state: "pending" }]);

    await unlink(path.join(data, "_playability.v1.json"));
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});
