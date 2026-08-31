import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import path from "node:path";
import { spawnSync } from "node:child_process";
import process from "node:process";
import test from "node:test";

const root = path.resolve(import.meta.dirname, "..");
const tool = path.join(root, "tools", "verification-proof.mjs");
const tempRoot = process.env.GRANDUMI_TEST_TEMP_ROOT;
if (!tempRoot) throw new Error("证明门禁测试必须设置 GRANDUMI_TEST_TEMP_ROOT。");
const policyFiles = [
  "verify.ps1",
  "tools/verification-proof.mjs",
  "tools/verify-protocol-contract.mjs",
  "tools/verify-card-content.mjs",
  "tools/card-content-lib.mjs",
  "tools/verify-mobile-browser.mjs",
  "tools/verification-proof.test.mjs",
  "tools/deploy-verification-gate.test.mjs",
  "deploy-test.ps1",
  "ops/server/deploy-test.sh",
  "protocol/contracts/websocket.v1.json",
  "卡牌数据/_schema.v1.json",
  "卡牌数据/_manifest.v1.json",
  "卡牌数据/_effect-registry.v1.json",
  "card-content/scenario-matrix.v1.json",
];

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}

function run(args, input, options = {}) {
  return spawnSync(process.execPath, [options.tool ?? tool, ...args], {
    cwd: options.cwd ?? root,
    encoding: "utf8",
    input,
  });
}

function git(args, cwd = root) {
  const result = spawnSync("git", args, { cwd, encoding: "utf8" });
  assert.equal(result.status, 0, result.stderr);
  return result.stdout.trim();
}

test("证明绑定同一提交与 tree，并拒绝错提交和篡改", async () => {
  const directory = await mkdtemp(path.join(tempRoot, "proof-test-"));
  try {
    const proofPath = path.join(directory, "proof.json");
    const invalidProofPath = path.join(directory, "invalid-proof.json");
    const commit = git(["rev-parse", "HEAD"]);
    const tree = git(["rev-parse", "HEAD^{tree}"]);
    const inputObject = {
      commit,
      tree,
      platform: "test",
      suites: [{ name: "契约测试", status: "passed", durationMs: 12 }],
    };
    const created = run(["create", "--output", proofPath], JSON.stringify(inputObject));
    assert.equal(created.status, 0, created.stderr);

    const wrongTree = run(
      ["create", "--output", invalidProofPath],
      JSON.stringify({ ...inputObject, tree: "2".repeat(40) }),
    );
    assert.notEqual(wrongTree.status, 0);
    assert.match(wrongTree.stderr, /与声明的 Git tree 不对应/);

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
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("策略摘要读取目标 Git tree，不受 Windows 与 Linux 行尾差异影响", async () => {
  const directory = await mkdtemp(path.join(tempRoot, "proof-eol-test-"));
  try {
    const fixtureTool = path.join(directory, "tools", "verification-proof.mjs");
    const lfPowerShell = "Write-Output 'line-one'\nWrite-Output 'line-two'\n";
    const crlfPowerShell = lfPowerShell.replaceAll("\n", "\r\n");

    for (const relativePath of policyFiles) {
      const destination = path.join(directory, relativePath);
      await mkdir(path.dirname(destination), { recursive: true });
      if (relativePath === "tools/verification-proof.mjs") {
        await writeFile(destination, await readFile(tool));
      } else if (relativePath.endsWith(".ps1")) {
        await writeFile(destination, lfPowerShell, "utf8");
      } else {
        await writeFile(destination, `fixture:${relativePath}\n`, "utf8");
      }
    }

    git(["init", "--quiet"], directory);
    git(["config", "user.name", "GrandUMI Verification Test"], directory);
    git(["config", "user.email", "verification-test@grand-umi.invalid"], directory);
    git(["config", "core.autocrlf", "false"], directory);
    git(["config", "commit.gpgsign", "false"], directory);
    git(["add", "--all"], directory);
    git(["commit", "--quiet", "-m", "test: verification policy fixture"], directory);

    const commit = git(["rev-parse", "HEAD"], directory);
    const tree = git(["rev-parse", "HEAD^{tree}"], directory);
    const committedBlob = git(["rev-parse", `${tree}:deploy-test.ps1`], directory);
    const powerShellPath = path.join(directory, "deploy-test.ps1");
    await writeFile(powerShellPath, crlfPowerShell, "utf8");
    assert.notEqual(
      git(["hash-object", "--no-filters", "deploy-test.ps1"], directory),
      committedBlob,
      "Windows CRLF 工作区字节应与 Git blob 不同。",
    );

    const proofPath = path.join(directory, "proof.json");
    const input = JSON.stringify({
      commit,
      tree,
      platform: "win32",
      suites: [{ name: "跨平台行尾", status: "passed", durationMs: 1 }],
    });
    const created = run(["create", "--output", proofPath], input, { cwd: directory, tool: fixtureTool });
    assert.equal(created.status, 0, created.stderr);

    await writeFile(powerShellPath, lfPowerShell, "utf8");
    assert.equal(
      git(["hash-object", "--no-filters", "deploy-test.ps1"], directory),
      committedBlob,
      "Linux LF 工作区字节应与 Git blob 相同。",
    );
    const bytes = await readFile(proofPath);
    const valid = run([
      "verify", "--proof", proofPath, "--commit", commit, "--tree", tree, "--checksum", sha256(bytes),
    ], undefined, { cwd: directory, tool: fixtureTool });
    assert.equal(valid.status, 0, valid.stderr);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});
