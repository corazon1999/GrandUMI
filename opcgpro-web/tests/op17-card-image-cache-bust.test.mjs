import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const testsDir = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(testsDir, "..");
const manifest = JSON.parse(
  readFileSync(path.join(webRoot, "public", "data", "imageManifest.json"), "utf8"),
);

test("OP17-017 与 OP17-018 使用修正后的独立缓存键", () => {
  assert.deepEqual(manifest["OP17-017"], [
    "/cards/op17/OP17-017.png?v=6d2855375eaa-r2",
  ]);
  assert.deepEqual(manifest["OP17-018"], [
    "/cards/op17/OP17-018.png?v=4492564aaa22-r2",
  ]);
  assert.notEqual(manifest["OP17-017"][0], manifest["OP17-018"][0]);
});
