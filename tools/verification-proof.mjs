import { createHash } from "node:crypto";
import { execFile } from "node:child_process";
import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { promisify } from "node:util";

const root = path.resolve(import.meta.dirname, "..");
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
  "deploy-hk.ps1",
  "ops/server/deploy-test.sh",
  "ops/server/deploy-grandumi-production-emergency.sh",
  "ops/server/grandumi-production-direct-proof.sh",
  "ops/server/activate-grandumi-production.sh",
  "ops/server/bootstrap-grandumi-production.sh",
  "ops/server/stage-grandumi-production.sh",
  "ops/server/build-grandumi-builtin-recovery-alias-manifest.sh",
  "ops/server/grandumi-production-drained-state.sh",
  "ops/server/grandumi-production-switch.sh",
  "protocol/contracts/websocket.v1.json",
  "卡牌数据/_schema.v1.json",
  "卡牌数据/_manifest.v1.json",
  "卡牌数据/_effect-registry.v1.json",
  "卡牌数据/_playability.v1.json",
  "card-content/scenario-matrix.v1.json"
];
const execFileAsync = promisify(execFile);
const maxGitOutputBytes = 32 * 1024 * 1024;
const completeSuiteCommands = [
  {
    category: "websocket-contract",
    matches: (command) => command === "node tools/verify-protocol-contract.mjs",
  },
  {
    category: "deployment-gates",
    matches: (command) => command === "node --test tools/verification-proof.test.mjs",
  },
  {
    category: "card-content",
    matches: (command) => command === "node tools/verify-card-content.mjs",
  },
  {
    category: "card-effect-audit",
    matches: (command) => command === "node tools/audit-card-effects.mjs --strict",
  },
  {
    // Windows PowerShell 5.1 可能损坏命令字段中的中文目录名，因此只固定 ASCII 边界。
    category: "server-tests",
    matches: (command) => /^dotnet test .*[\\/]GrandUMIServer\.Tests\.csproj$/.test(command),
  },
  {
    category: "frontend-tests",
    matches: (command) => command === "node --test opcgpro-web/tests/*.test.mjs",
  },
  {
    category: "qq-bot-tests",
    matches: (command) => command === "python -m unittest discover -s qq-bug-bot/tests",
  },
  {
    category: "frontend-build",
    matches: (command) => command === "npm run build --prefix opcgpro-web",
  },
  {
    category: "mobile-browser",
    matches: (command) => command === "node tools/verify-mobile-browser.mjs",
  },
];

function sha256(value) {
  return createHash("sha256").update(value).digest("hex");
}

function stable(value) {
  if (Array.isArray(value)) return `[${value.map(stable).join(",")}]`;
  if (value && typeof value === "object") {
    return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${stable(value[key])}`).join(",")}}`;
  }
  return JSON.stringify(value);
}

async function gitBytes(args, failureMessage) {
  try {
    const { stdout } = await execFileAsync("git", args, {
      cwd: root,
      encoding: null,
      maxBuffer: maxGitOutputBytes,
      windowsHide: true,
    });
    return stdout;
  } catch (error) {
    const stderr = Buffer.isBuffer(error?.stderr)
      ? error.stderr.toString("utf8").trim()
      : String(error?.stderr ?? "").trim();
    throw new Error(`${failureMessage}${stderr ? `：${stderr}` : "。"}`);
  }
}

async function assertCommitTree(commit, expectedTree, label) {
  const actualTree = (await gitBytes(
    ["rev-parse", "--verify", `${commit}^{tree}`],
    `无法解析${label}的 Git tree`,
  )).toString("ascii").trim();
  if (actualTree !== expectedTree) throw new Error(`${label}与声明的 Git tree 不对应。`);
}

async function policyDigest(tree) {
  const parts = [];
  for (const relativePath of policyFiles) {
    const canonicalPath = relativePath.replaceAll("\\", "/");
    const content = await gitBytes(
      ["cat-file", "blob", `${tree}:${canonicalPath}`],
      `无法读取目标 Git tree 中的策略文件 ${canonicalPath}`,
    );
    parts.push(`${canonicalPath}\0${content.length}\0`);
    parts.push(content);
  }
  return sha256(Buffer.concat(parts.map((part) => Buffer.isBuffer(part) ? part : Buffer.from(part))));
}

function argument(name) {
  const index = process.argv.indexOf(name);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

function assertCommit(value, label) {
  if (!/^[0-9a-f]{40}$/.test(value ?? "")) throw new Error(`${label} 必须是完整 40 位小写提交号。`);
}

function assertCompleteVerificationSuites(suites) {
  if (!Array.isArray(suites) || suites.length !== completeSuiteCommands.length) {
    throw new Error(`Direct 正式发布证明必须包含 ${completeSuiteCommands.length} 个完整验证类别。`);
  }

  const matched = new Set();
  for (const suite of suites) {
    if (!suite || suite.status !== "passed" || typeof suite.command !== "string") {
      throw new Error("Direct 正式发布证明的全部验证类别都必须通过并记录命令。");
    }
    const categories = completeSuiteCommands.filter((item) => item.matches(suite.command));
    if (categories.length !== 1) {
      throw new Error(`Direct 正式发布证明包含未知验证命令：${suite.command}`);
    }
    const category = categories[0].category;
    if (matched.has(category)) {
      throw new Error(`Direct 正式发布证明重复了验证类别：${category}`);
    }
    matched.add(category);
  }

  const missing = completeSuiteCommands
    .map((item) => item.category)
    .filter((category) => !matched.has(category));
  if (missing.length > 0) {
    throw new Error(`Direct 正式发布证明缺少验证类别：${missing.join("、")}`);
  }
}

async function readStdin() {
  const chunks = [];
  for await (const chunk of process.stdin) chunks.push(chunk);
  return Buffer.concat(chunks).toString("utf8").replace(/^\uFEFF/, "");
}

async function createProof() {
  const output = argument("--output");
  if (!output) throw new Error("create 缺少 --output。");
  const input = JSON.parse(await readStdin());
  assertCommit(input.commit, "proof.commit");
  assertCommit(input.tree, "proof.tree");
  if (!Array.isArray(input.suites) || input.suites.length === 0) throw new Error("proof.suites 不能为空。");
  if (input.suites.some((suite) => suite.status !== "passed")) throw new Error("只有全部通过的验证才能生成证明。");
  await assertCommitTree(input.commit, input.tree, "proof.commit");

  const payload = {
    schemaVersion: "grandumi.verification-proof.v1",
    commit: input.commit,
    tree: input.tree,
    generatedAtUtc: new Date().toISOString(),
    platform: input.platform ?? process.platform,
    suites: input.suites,
    policyDigest: await policyDigest(input.tree),
  };
  const proof = { ...payload, payloadSha256: sha256(stable(payload)) };
  await writeFile(output, `${JSON.stringify(proof, null, 2)}\n`, { encoding: "utf8", flag: "wx" });
  console.log(`验证证明已生成：${output}`);
}

async function verifyProof() {
  const proofPath = argument("--proof");
  const expectedCommit = argument("--commit");
  const expectedTree = argument("--tree");
  const expectedFileSha = argument("--checksum");
  if (!proofPath || !expectedFileSha) throw new Error("verify 缺少 --proof 或 --checksum。");
  assertCommit(expectedCommit, "expected commit");
  assertCommit(expectedTree, "expected tree");
  if (!/^[0-9a-f]{64}$/.test(expectedFileSha)) throw new Error("checksum 必须是 64 位小写 SHA-256。");

  const bytes = await readFile(proofPath);
  if (sha256(bytes) !== expectedFileSha) throw new Error("证明文件 SHA-256 与传输摘要不一致。");
  const proof = JSON.parse(bytes.toString("utf8"));
  const { payloadSha256, ...payload } = proof;
  if (proof.schemaVersion !== "grandumi.verification-proof.v1") throw new Error("验证证明版本不受支持。");
  if (proof.commit !== expectedCommit || proof.tree !== expectedTree) throw new Error("验证证明不属于待部署提交或其 Git tree。");
  await assertCommitTree(expectedCommit, expectedTree, "待部署提交");
  if (proof.policyDigest !== await policyDigest(expectedTree)) throw new Error("验证策略与待部署提交不一致。");
  if (!Array.isArray(proof.suites) || proof.suites.length === 0 || proof.suites.some((suite) => suite.status !== "passed")) {
    throw new Error("验证证明未记录全部通过的测试套件。");
  }
  if (process.argv.includes("--require-complete")) assertCompleteVerificationSuites(proof.suites);
  if (payloadSha256 !== sha256(stable(payload))) throw new Error("验证证明内容摘要无效。");
  console.log(`验证证明有效：${proof.commit}，${proof.suites.length} 个套件全部通过。`);
}

try {
  const command = process.argv[2];
  if (command === "create") await createProof();
  else if (command === "verify") await verifyProof();
  else throw new Error("用法：verification-proof.mjs <create|verify>。");
} catch (error) {
  console.error(`验证证明失败：${error instanceof Error ? error.message : String(error)}`);
  process.exit(1);
}
