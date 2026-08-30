import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdtemp, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { spawnSync } from "node:child_process";
import process from "node:process";
import test from "node:test";

const root = path.resolve(import.meta.dirname, "..");
const tool = path.join(root, "tools", "verification-proof.mjs");
const tempRoot = process.env.GRANDUMI_TEST_TEMP_ROOT;
if (!tempRoot) throw new Error("证明门禁测试必须设置 GRANDUMI_TEST_TEMP_ROOT。");

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}

function run(args, input) {
  return spawnSync(process.execPath, [tool, ...args], {
    cwd: root,
    encoding: "utf8",
    input,
  });
}

test("证明绑定同一提交与 tree，并拒绝错提交和篡改", async () => {
  const directory = await mkdtemp(path.join(tempRoot, "proof-test-"));
  const proofPath = path.join(directory, "proof.json");
  const commit = "1".repeat(40);
  const tree = "2".repeat(40);
  const input = JSON.stringify({
    commit,
    tree,
    platform: "test",
    suites: [{ name: "契约测试", status: "passed", durationMs: 12 }],
  });
  const created = run(["create", "--output", proofPath], input);
  assert.equal(created.status, 0, created.stderr);

  const bytes = await readFile(proofPath);
  const checksum = sha256(bytes);
  const valid = run(["verify", "--proof", proofPath, "--commit", commit, "--tree", tree, "--checksum", checksum]);
  assert.equal(valid.status, 0, valid.stderr);

  const wrongCommit = run([
    "verify", "--proof", proofPath, "--commit", "3".repeat(40), "--tree", tree, "--checksum", checksum,
  ]);
  assert.notEqual(wrongCommit.status, 0);
  assert.match(wrongCommit.stderr, /不属于待部署提交/);

  const tampered = JSON.parse(bytes.toString("utf8"));
  tampered.suites[0].durationMs = 1;
  await writeFile(proofPath, `${JSON.stringify(tampered, null, 2)}\n`, "utf8");
  const tamperedBytes = await readFile(proofPath);
  const rejected = run([
    "verify", "--proof", proofPath, "--commit", commit, "--tree", tree, "--checksum", sha256(tamperedBytes),
  ]);
  assert.notEqual(rejected.status, 0);
  assert.match(rejected.stderr, /内容摘要无效/);
});
