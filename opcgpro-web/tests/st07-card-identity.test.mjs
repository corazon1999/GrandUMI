import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const webRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

test("ST07-015 与 ST07-016 的费用和简中卡图身份一致", async () => {
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
