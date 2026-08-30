import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const webRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const physicalAssetTest = process.env.GRANDUMI_REPOSITORY_VERIFICATION === "1"
  ? { skip: "仓库验证只校验 ST07 卡图映射；测试服部署负责实体卡图内容" }
  : {};

test("ST07-015 与 ST07-016 的费用和简中卡图映射一致", async () => {
  const cards = JSON.parse(await readFile(path.join(webRoot, "public", "data", "ST07.json"), "utf8"));
  const manifest = JSON.parse(
    await readFile(path.join(webRoot, "public", "data", "imageManifest.json"), "utf8"),
  );
  const soulPocus = cards.find((card) => card.number === "ST07-015");
  const powerMochi = cards.find((card) => card.number === "ST07-016");

  assert.equal(soulPocus?.name, "对魂低语");
  assert.equal(soulPocus?.cost, "5");
  assert.equal(powerMochi?.name, "强力麻糬");
  assert.equal(powerMochi?.cost, "1");
  assert.deepEqual(manifest["ST07-015"], ["/cards/st07/ST07-016.png"]);
  assert.deepEqual(manifest["ST07-016"], ["/cards/st07/ST07-015.png"]);
});

test("ST07-015 与 ST07-016 的实体卡图内容一致", physicalAssetTest, async () => {
  const manifest = JSON.parse(
    await readFile(path.join(webRoot, "public", "data", "imageManifest.json"), "utf8"),
  );
  const soulPocusImage = await readFile(path.join(webRoot, "public", manifest["ST07-015"][0]));
  const powerMochiImage = await readFile(path.join(webRoot, "public", manifest["ST07-016"][0]));
  assert.equal(
    createHash("sha256").update(soulPocusImage).digest("hex"),
    "1fa5836b811c577a281bd06bf19ee837eabcc4a7486725ceaf847977856e8af1",
  );
  assert.equal(
    createHash("sha256").update(powerMochiImage).digest("hex"),
    "0750f52da66873808e3d6a0409ddc2217740562ebd4e3516865e2660e0c725ba",
  );
});
