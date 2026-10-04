import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { spawnSync } from "node:child_process";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import test from "node:test";

const root = path.resolve(import.meta.dirname, "..");
const tempRoot = process.env.GRANDUMI_TEST_TEMP_ROOT;
if (!tempRoot) throw new Error("部署门禁测试必须设置 GRANDUMI_TEST_TEMP_ROOT。");

const verificationPolicyFiles = [
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
  "card-content/scenario-matrix.v1.json",
];

const completeVerificationSuites = [
  "node tools/verify-protocol-contract.mjs",
  "node --test tools/verification-proof.test.mjs",
  "node tools/verify-card-content.mjs",
  "node tools/audit-card-effects.mjs --strict",
  // 使用损坏的中文目录模拟 Windows PowerShell 5.1 证明，ASCII 边界仍必须可识别。
  "dotnet test ???/GrandUMIServer.Tests.csproj",
  "node --test opcgpro-web/tests/*.test.mjs",
  "python -m unittest discover -s qq-bug-bot/tests",
  "npm run build --prefix opcgpro-web",
  "node tools/verify-mobile-browser.mjs",
].map((command, index) => ({
  name: `fixture-suite-${index + 1}`,
  command,
  status: "passed",
  durationMs: index + 1,
}));

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

function git(args, cwd) {
  const result = spawnSync("git", args, { cwd, encoding: "utf8" });
  assert.equal(result.status, 0, result.stderr);
  return result.stdout.trim();
}

function resolveBash() {
  const direct = spawnSync("bash", ["--version"], { encoding: "utf8" });
  if (direct.status === 0) return "bash";

  const gitExecPath = spawnSync("git", ["--exec-path"], { encoding: "utf8" });
  assert.equal(gitExecPath.status, 0, gitExecPath.stderr);
  const candidates = process.platform === "win32"
    ? [
        path.resolve(gitExecPath.stdout.trim(), "../../../bin/bash.exe"),
        path.resolve(gitExecPath.stdout.trim(), "../../../usr/bin/bash.exe"),
      ]
    : ["/bin/bash", "/usr/bin/bash"];
  for (const candidate of candidates) {
    const probe = spawnSync(candidate, ["--version"], { encoding: "utf8" });
    if (probe.status === 0) return candidate;
  }
  assert.fail("部署锁行为门禁需要可用的 Bash。");
}

function resolveBashPath(bash, inputPath) {
  if (process.platform !== "win32") return inputPath;
  const result = spawnSync(
    bash,
    [
      "-c",
      'if command -v cygpath >/dev/null; then cygpath -u "$1"; '
        + 'elif command -v wslpath >/dev/null; then wslpath -a "$1"; else exit 127; fi',
      "grandumi-bash-path",
      inputPath,
    ],
    { encoding: "utf8" },
  );
  assert.equal(result.status, 0, result.stderr || result.stdout);
  return result.stdout.trim();
}

function checkRecoveryCompatibility(
  bash,
  helperPath,
  repository,
  sourceCommit,
  targetCommit,
  changedPath,
) {
  return spawnSync(
    bash,
    [
      "-c",
      'source "$1"\n'
        + 'grandumi_is_builtin_recovery_compatible_change "$2" "$3" "$4" "$5"',
      "grandumi-recovery-compat-test",
      helperPath,
      repository,
      sourceCommit,
      targetCommit,
      changedPath,
    ],
    { encoding: "utf8" },
  );
}

async function writeTrackedFile(repository, relativePath, content) {
  const destination = path.join(repository, relativePath);
  await mkdir(path.dirname(destination), { recursive: true });
  await writeFile(destination, content, "utf8");
}

function assertSshKeepAlive(source, entryName) {
  const sshCalls = source.split(/\r?\n/).filter((line) => line.includes("& $ssh"));
  assert.ok(sshCalls.length > 0, `${entryName} 必须包含 SSH 调用。`);
  for (const call of sshCalls) {
    assert.match(
      call,
      /& \$ssh (?=[^\r\n]*-o BatchMode=yes)(?=[^\r\n]*-o ServerAliveInterval=30)(?=[^\r\n]*-o ServerAliveCountMax=3)[^\r\n]*\$Server/,
      `${entryName} 的每个 SSH 调用都必须启用批处理和连接保活：${call.trim()}`,
    );
  }
}

function assertScpKeepAlive(source, entryName) {
  const scpCalls = source.split(/\r?\n/).filter((line) => line.includes("& $scp"));
  assert.ok(scpCalls.length > 0, `${entryName} 必须包含 SCP 调用。`);
  for (const call of scpCalls) {
    assert.match(
      call,
      /& \$scp (?=[^\r\n]*-o BatchMode=yes)(?=[^\r\n]*-o ServerAliveInterval=30)(?=[^\r\n]*-o ServerAliveCountMax=3)/,
      `${entryName} 的每个 SCP 调用都必须启用批处理和连接保活：${call.trim()}`,
    );
  }
}

function assertBoundedOpenSsh(source, commandVariable, entryName) {
  const calls = source.split(/\r?\n/).filter((line) => line.includes(`& $${commandVariable}`));
  assert.ok(calls.length > 0, `${entryName} 必须包含 ${commandVariable.toUpperCase()} 调用。`);
  for (const call of calls) {
    assert.match(call, /-o ConnectTimeout=15/, `${entryName} 必须限制连接建立时间：${call.trim()}`);
    assert.match(call, /-o ConnectionAttempts=1/, `${entryName} 必须限制连接尝试次数：${call.trim()}`);
  }
}

test("Windows 发布入口在推送前完成验证，并把同提交证明交给服务器", async () => {
  const source = await readFile(path.join(root, "deploy-test.ps1"), "utf8");
  const verifyAt = source.indexOf('"verify.ps1"');
  const pushAt = source.indexOf("& $git push origin main");
  const deployAt = source.indexOf("ops/server/deploy-test.sh");
  assert.ok(verifyAt >= 0 && pushAt > verifyAt, "完整验证必须发生在 git push 之前。");
  assert.ok(deployAt > pushAt, "服务器部署必须发生在验证和推送之后。");
  assert.match(source, /-ExpectedCommit \$target -ProofPath \$proof/);
  assert.match(source, /'\$remoteProof' '\$proofChecksum'/);
  assertSshKeepAlive(source, "测试服发布入口");
});

test("正式服入口只发布精确 main，并显式区分排空与紧急 A/B 模式", async () => {
  const source = await readFile(path.join(root, "deploy-hk.ps1"), "utf8");
  const pushAt = source.indexOf('& $git push origin "${verifiedHead}:refs/heads/main"');
  const remoteFetchAt = source.indexOf("git -C /opt/grandumi fetch --force --prune");
  const deployAt = source.indexOf("deploy-grandumi-production-emergency.sh");
  const normalizeAt = source.indexOf('$remoteDeploy = $remoteDeploy.Replace("`r", "")');
  const remoteExecuteAt = source.indexOf(
    "& $ssh -o BatchMode=yes -o ConnectTimeout=15 -o ConnectionAttempts=1 -o ServerAliveInterval=30 -o ServerAliveCountMax=3 $Server $remoteDeploy",
  );
  const directVerifyAt = source.indexOf('"tools/verification-proof.mjs" verify');

  assert.match(source, /\[switch\]\$Emergency/);
  assert.match(source, /\[switch\]\$Drained/);
  assert.match(source, /\[switch\]\$Direct/);
  assert.match(source, /\[string\]\$ProofPath/);
  assert.match(source, /\[bool\]\$Drained -eq \[bool\]\$Emergency/);
  assert.match(source, /\$Direct -and -not \$Drained/);
  assert.match(source, /-not \$Direct -and \$ProofPath/);
  assert.match(source, /root@186\.241\.65\.7/);
  assert.match(source, /\$Server -ne "root@186\.241\.65\.7"/);
  assert.match(source, /direct\.grand-umi\.com/);
  assert.match(source, /\$remoteMode = if \(\$Drained\) \{ "--drained" \} else \{ "--emergency" \}/);
  assert.match(source, /git merge --ff-only refs\/remotes\/origin\/main/);
  assert.match(source, /\$prePushHead -ne \$verifiedHead -or \$prePushDirty/);
  assert.match(source, /\$originHead -ne \$verifiedHead/);
  assert.match(source, /ls-tree -r --name-only \$localHead -- changelog-cache\/pending/);
  assert.ok(pushAt >= 0 && remoteFetchAt > pushAt && deployAt > remoteFetchAt,
    "必须先精确推送，再只更新远端 Git ref，最后执行版本化发布脚本。");
  assert.ok(directVerifyAt >= 0 && directVerifyAt < pushAt,
    "Direct 完整九套件证明必须在 git push 之前通过本地验证。");
  assert.ok(normalizeAt > deployAt && remoteExecuteAt > normalizeAt,
    "Windows 入口必须在交给 Linux shell 前移除远程命令中的 CR。");
  assert.match(source, /git -C \/opt\/grandumi show '\$\{localHead\}:\$serverScriptPath'/);
  assert.match(source, /--require-complete/);
  assert.match(source, /grandumi-direct-proof-\$shortHead-\$nonce\.json/);
  assert.match(source, /--direct-proof '\$remoteProof' '\$proofChecksum'/);
  assert.match(source, /bash "`\$script" '\$remoteMode' '\$localHead'\$remoteDirectArguments/);
  assertSshKeepAlive(source, "正式服发布入口");
  assertScpKeepAlive(source, "正式服 Direct 证明上传入口");
  assertBoundedOpenSsh(source, "ssh", "正式服发布入口");
  assertBoundedOpenSsh(source, "scp", "正式服 Direct 证明上传入口");
  assert.doesNotMatch(source, /git add -A/);
  assert.doesNotMatch(source, /git pull --no-rebase/);
  assert.doesNotMatch(source, /\/opt\/grandumi\/deploy\.sh/);
  assert.doesNotMatch(source, /git[^\n]*(?:checkout|reset --hard)/);
});

test("Direct 正式发布只接受绑定目标提交与全部九类命令的真实验证证明", async () => {
  const bash = resolveBash();
  const directory = await mkdtemp(path.join(tempRoot, "direct-proof-test-"));
  try {
    for (const relativePath of verificationPolicyFiles) {
      const destination = path.join(directory, relativePath);
      await mkdir(path.dirname(destination), { recursive: true });
      await writeFile(destination, await readFile(path.join(root, relativePath)));
    }

    git(["init", "--quiet"], directory);
    git(["config", "user.name", "GrandUMI Direct Proof Test"], directory);
    git(["config", "user.email", "direct-proof-test@grand-umi.invalid"], directory);
    git(["config", "core.autocrlf", "false"], directory);
    git(["config", "commit.gpgsign", "false"], directory);
    git(["add", "--all"], directory);
    git(["commit", "--quiet", "-m", "test: direct production proof fixture"], directory);

    const target = git(["rev-parse", "HEAD"], directory);
    const tree = git(["rev-parse", "HEAD^{tree}"], directory);
    const fixtureTool = path.join(directory, "tools", "verification-proof.mjs");
    const helper = resolveBashPath(
      bash,
      path.join(directory, "ops", "server", "grandumi-production-direct-proof.sh"),
    );
    const bashDirectory = resolveBashPath(bash, directory);
    const uidResult = spawnSync(bash, ["-c", "id -u"], { encoding: "utf8" });
    assert.equal(uidResult.status, 0, uidResult.stderr);
    const expectedUid = uidResult.stdout.trim();
    let nonce = 0;

    async function createProof(suites) {
      nonce += 1;
      const proofPath = path.join(
        directory,
        `grandumi-direct-proof-${target.slice(0, 12)}-${nonce.toString(16).padStart(32, "0")}.json`,
      );
      const created = spawnSync(
        process.execPath,
        [fixtureTool, "create", "--output", proofPath],
        {
          cwd: directory,
          encoding: "utf8",
          input: JSON.stringify({ commit: target, tree, platform: "fixture", suites }),
        },
      );
      assert.equal(created.status, 0, created.stderr);
      return proofPath;
    }

    function runHelper(proofPath, checksum, targetArgument = target) {
      const bashProof = resolveBashPath(bash, proofPath);
      const chmod = spawnSync(bash, ["-c", 'chmod 0600 "$1"', "direct-proof-chmod", bashProof], {
        encoding: "utf8",
      });
      assert.equal(chmod.status, 0, chmod.stderr);
      const mode = spawnSync(bash, ["-c", 'stat -c "%a" "$1"', "direct-proof-stat", bashProof], {
        encoding: "utf8",
      });
      assert.equal(mode.status, 0, mode.stderr);
      return spawnSync(
        bash,
        [
          "-c",
          [
            "set -Eeuo pipefail",
            'source "$1"',
            'grandumi_verify_complete_direct_proof "$2" "$3" "$4" "$5" "$6" "$7" "$8" "$9"',
          ].join("\n"),
          "grandumi-direct-proof-test",
          helper,
          bashDirectory,
          bashDirectory,
          targetArgument,
          bashProof,
          checksum,
          bashDirectory,
          expectedUid,
          mode.stdout.trim(),
        ],
        { encoding: "utf8" },
      );
    }

    async function checksumOf(proofPath) {
      return sha256(await readFile(proofPath));
    }

    async function forgeProof(proofPath, mutate, recomputePayload) {
      const proof = JSON.parse(await readFile(proofPath, "utf8"));
      mutate(proof);
      if (recomputePayload) {
        const { payloadSha256: _oldPayloadSha256, ...payload } = proof;
        proof.payloadSha256 = sha256(stable(payload));
      }
      await writeFile(proofPath, `${JSON.stringify(proof, null, 2)}\n`, "utf8");
      return checksumOf(proofPath);
    }

    const validProof = await createProof(completeVerificationSuites);
    const validChecksum = await checksumOf(validProof);
    const accepted = runHelper(validProof, validChecksum);
    assert.equal(accepted.status, 0, accepted.stderr || accepted.stdout);
    assert.match(accepted.stdout, /9 个套件全部通过/);

    const infrastructureOnly = await createProof(completeVerificationSuites.slice(0, 2));
    const partial = runHelper(infrastructureOnly, await checksumOf(infrastructureOnly));
    assert.notEqual(partial.status, 0, "基础设施部分证明不得用于 Direct 正式发布。");
    assert.match(partial.stderr, /必须包含 9 个完整验证类别/);

    const duplicateProof = await createProof(
      completeVerificationSuites.map(() => ({ ...completeVerificationSuites[0] })),
    );
    const duplicate = runHelper(duplicateProof, await checksumOf(duplicateProof));
    assert.notEqual(duplicate.status, 0, "重复同一套件凑满九项必须失败关闭。");
    assert.match(duplicate.stderr, /重复了验证类别/);

    const wrongChecksum = runHelper(validProof, "0".repeat(64));
    assert.notEqual(wrongChecksum.status, 0, "错误的完整文件 SHA-256 必须失败关闭。");
    assert.match(wrongChecksum.stderr, /文件 SHA-256 与传输摘要不一致/);

    const wrongCommitProof = await createProof(completeVerificationSuites);
    const wrongCommitChecksum = await forgeProof(
      wrongCommitProof,
      (proof) => { proof.commit = "1".repeat(40); },
      true,
    );
    const wrongCommit = runHelper(wrongCommitProof, wrongCommitChecksum);
    assert.notEqual(wrongCommit.status, 0, "错提交证明必须失败关闭。");
    assert.match(wrongCommit.stderr, /不属于待部署提交或其 Git tree/);

    const wrongTreeProof = await createProof(completeVerificationSuites);
    const wrongTreeChecksum = await forgeProof(
      wrongTreeProof,
      (proof) => { proof.tree = "2".repeat(40); },
      true,
    );
    const wrongTree = runHelper(wrongTreeProof, wrongTreeChecksum);
    assert.notEqual(wrongTree.status, 0, "错 Git tree 证明必须失败关闭。");
    assert.match(wrongTree.stderr, /不属于待部署提交或其 Git tree/);

    const wrongPolicyProof = await createProof(completeVerificationSuites);
    const wrongPolicyChecksum = await forgeProof(
      wrongPolicyProof,
      (proof) => { proof.policyDigest = "3".repeat(64); },
      true,
    );
    const wrongPolicy = runHelper(wrongPolicyProof, wrongPolicyChecksum);
    assert.notEqual(wrongPolicy.status, 0, "错策略摘要证明必须失败关闭。");
    assert.match(wrongPolicy.stderr, /验证策略与待部署提交不一致/);

    const wrongPayloadProof = await createProof(completeVerificationSuites);
    const wrongPayloadChecksum = await forgeProof(
      wrongPayloadProof,
      (proof) => { proof.suites[0].durationMs = 999999; },
      false,
    );
    const wrongPayload = runHelper(wrongPayloadProof, wrongPayloadChecksum);
    assert.notEqual(wrongPayload.status, 0, "错误 payload 摘要证明必须失败关闭。");
    assert.match(wrongPayload.stderr, /内容摘要无效/);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("服务器发布保留紧急模式，并为正常发布增加多阶段排空门禁", async () => {
  const source = await readFile(
    path.join(root, "ops", "server", "deploy-grandumi-production-emergency.sh"),
    "utf8",
  );
  const activate = await readFile(
    path.join(root, "ops", "server", "activate-grandumi-production.sh"),
    "utf8",
  );
  const switching = await readFile(
    path.join(root, "ops", "server", "grandumi-production-switch.sh"),
    "utf8",
  );

  assert.match(source, /--emergency\|--drained\|--preflight/);
  assert.match(source, /"\$\{3:-\}" == --direct-proof/);
  assert.match(source, /"\$mode" == --drained/);
  assert.match(source, /grandumi_verify_complete_direct_proof/);
  assert.match(source, /verify_production_account_authority/);
  assert.match(source, /"\$mode" == --preflight/);
  assert.match(source, /flock -n 8/);
  assert.match(source, /flock -n 9/);
  assert.match(source, /git ls-remote "\$git_url" refs\/heads\/main/);
  assert.match(source, /"\$published_main" == "\$target"/);
  assert.match(source, /"\$remote_main" == "\$target"/);
  assert.match(source, /test-deployed/);
  assert.match(source, /test-verified\.json/);
  assert.match(source, /grandumi\.verification-proof\.v1/);
  assert.match(source, /proof\.get\("commit"\) != target/);
  assert.match(source, /proof\.get\("tree"\) != target_tree/);
  assert.match(source, /item\.get\("status"\) != "passed"/);
  assert.match(source, /ls-tree -r --name-only "\$target" -- changelog-cache\/pending/);
  assert.match(source, /merge-base --is-ancestor "\$deployed" "\$target"/);
  assert.match(source, /"\$current_commit" == "\$deployed"/);
  assert.match(source, /shared_dir\/accounts\.db/);
  assert.match(source, /shared_dir\/prepared/);
  assert.match(source, /shared_dir\/active/);
  assert.match(source, /grandumi-shared-account-migration verify-test/);
  assert.match(source, /grandumi-shared-account-migration[\s\\]+\n\s+verify-target/);
  assert.match(source, /systemctl is-active --quiet grandumi-test-backend\.service/);
  assert.match(source, /GRANDUMI_ACCOUNT_DB=\/data\/grandumi-shared\/accounts\.db/);
  assert.match(source, /find \/var\/lib\/grandumi-admin-deploy\/requests/);
  assert.match(source, /journalQueueDepth/);
  assert.match(source, /snapshotQueueDepth/);
  assert.match(source, /worktree add --detach/);
  assert.match(source, /worktree remove --force/);
  assert.match(source, /source "\$worktree\/ops\/server\/grandumi-production-drained-state\.sh"/);
  assert.match(source, /"\$mode" == --drained/);
  assert.match(source, /grandumi_verify_production_drained_state "正式发布阶段排空复核"/);
  assert.match(source, /GRANDUMI_PRODUCTION_DRAINED=/);
  assert.match(source, /GRANDUMI_PRODUCTION_DIRECT="\$direct_release"/);
  assert.doesNotMatch(source, /git[^\n]*(?:checkout|reset --hard)/);
  assert.doesNotMatch(source, /get\("rooms"\)|get\("maintenance"\)/);

  const firstGateAt = source.indexOf("\nverify_release_candidate\n");
  const bootstrapAt = source.indexOf("bootstrap-grandumi-production.sh", firstGateAt);
  const stageAt = source.indexOf("stage-grandumi-production.sh", bootstrapAt);
  const secondGateAt = source.indexOf("\nverify_release_candidate\n", firstGateAt + 1);
  const activateAt = source.indexOf("activate-grandumi-production.sh", secondGateAt);
  const postStateAt = source.indexOf("\nverify_production_state\n", activateAt);
  assert.ok(firstGateAt >= 0, "构建前必须执行完整候选门禁。");
  assert.ok(bootstrapAt > firstGateAt && stageAt > bootstrapAt, "必须先从目标 worktree 引导，再预构建发布包。");
  assert.ok(secondGateAt > stageAt, "耗时构建后、切流前必须重新读取所有易变门禁。 ");
  assert.ok(activateAt > secondGateAt && postStateAt > activateAt, "激活后必须再次核验权威版本和槽位。 ");

  assert.match(activate, /grandumi-production-snapshot "\$target"/);
  assert.match(activate, /\.complete/);
  assert.match(activate, /GRANDUMI_PRODUCTION_DIRECT="\$direct_release"[\s\\]+\n\s+\/usr\/local\/sbin\/grandumi-production-switch --release "\$target"/);
  assert.match(activate, /切换脚本自动回滚/);
  assert.match(activate, /Direct 正式发布只允许在共享账号权威已经激活后执行/);
  assert.match(activate, /Direct 正式发布只允许从健康的现有 A\/B 正式槽切换/);
  assert.match(activate, /"\$shared_migration" verify-target "\$repo\/releases\/\$target\/backend"/);
  assert.match(activate, /else\s+"\$shared_migration" verify-test\s+fi/);
  assert.ok(
    activate.indexOf("Direct 正式发布不得进入旧单槽或首次共享账号迁移路径")
      < activate.indexOf('"$shared_migration" activate-test'),
    "Direct 必须在任何首次迁移或测试服激活路径前失败关闭。",
  );

  assert.match(switching, /Direct 正式发布不允许用于自动故障转移/);
  assert.match(switching, /verify_direct_account_authority_after_stop/);
  assert.match(switching, /if \[\[ "\$direct_release" == 1 \]\]; then\s+verify_direct_account_authority_after_stop/);
  assert.match(switching, /else\s+"\$shared_migration" prepare/);
  assert.match(switching, /"\$direct_release" == 0[\s\\]+\n\s+&& \( "\$mode" == --release \|\| -f "\$shared_active_marker" \)/);
});

test("Direct 切槽在旧后端停写后复核 active，缺失时不触发迁移或测试服调用", async () => {
  const bash = resolveBash();
  const source = await readFile(
    path.join(root, "ops", "server", "grandumi-production-switch.sh"),
    "utf8",
  );
  const functionMatch = source.match(/verify_direct_account_authority_after_stop\(\) \{[\s\S]*?\n\}/);
  assert.ok(functionMatch, "必须定义 Direct 停写后的账号权威复核函数。");

  const directory = await mkdtemp(path.join(tempRoot, "direct-authority-test-"));
  try {
    const active = path.join(directory, "active");
    const calls = path.join(directory, "migration-calls.txt");
    const migration = path.join(directory, "migration.sh");
    await writeFile(
      migration,
      `#!/usr/bin/env bash\nprintf '%s\\n' "$*" >> '${resolveBashPath(bash, calls)}'\n`,
      "utf8",
    );
    const bashMigration = resolveBashPath(bash, migration);
    const bashActive = resolveBashPath(bash, active);
    const runFixture = () => spawnSync(
      bash,
      [
        "-c",
        [
          "set -Eeuo pipefail",
          'die() { echo "错误：$*" >&2; exit 1; }',
          'shared_active_marker="$1"',
          'shared_migration="$2"',
          functionMatch[0],
          'verify_direct_account_authority_after_stop "/opt/grandumi/releases/target/backend"',
        ].join("\n"),
        "grandumi-direct-authority-test",
        bashActive,
        bashMigration,
      ],
      { encoding: "utf8" },
    );

    const chmod = spawnSync(bash, ["-c", 'chmod 0700 "$1"', "direct-authority-chmod", bashMigration], {
      encoding: "utf8",
    });
    assert.equal(chmod.status, 0, chmod.stderr);

    const missing = runFixture();
    assert.notEqual(missing.status, 0, "active 缺失时必须在迁移工具运行前失败关闭。");
    assert.match(missing.stderr, /active 标记缺失/);
    await assert.rejects(readFile(calls, "utf8"), /ENOENT/);

    await writeFile(active, "active\n", "utf8");
    const valid = runFixture();
    assert.equal(valid.status, 0, valid.stderr || valid.stdout);
    assert.equal(
      await readFile(calls, "utf8"),
      "verify-target /opt/grandumi/releases/target/backend\n",
      "Direct 临界复核只能验证正式目标，不得 prepare、commit、verify-test 或 activate-test。",
    );
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("正式发布为持久房间恢复提供有界就绪窗口，崩溃或超时仍自动回退", async () => {
  const bash = resolveBash();
  const source = await readFile(
    path.join(root, "ops", "server", "grandumi-production-switch.sh"),
    "utf8",
  );
  const functionMatch = source.match(/wait_for_backend_ready\(\) \{[\s\S]*?\n\}/);
  assert.ok(functionMatch, "必须定义有界后端就绪等待。 ");
  const waitFunction = functionMatch[0];

  assert.match(source, /GRANDUMI_PRODUCTION_BACKEND_READY_TIMEOUT_SECONDS:-600/);
  assert.match(source, /backend_ready_timeout_seconds >= 60 && backend_ready_timeout_seconds <= 900/);
  assert.match(waitFunction, /while \(\( elapsed < backend_ready_timeout_seconds \)\)/);
  assert.match(waitFunction, /--max-time 1/);
  assert.match(waitFunction, /systemctl is-active --quiet "\$unit"/);
  assert.match(waitFunction, /current_pid.*expected_pid/);
  assert.match(waitFunction, /version.get\("commit"\) != expected/);
  assert.match(waitFunction, /return 1/);
  assert.doesNotMatch(waitFunction, /while\s+true/);

  assert.match(
    source,
    /wait_for_backend_ready "\$target" "\$backend_port" "\$release"\nproxy_switch_started=1\nwrite_proxy/,
  );
  assert.doesNotMatch(source, /wait_for_backend_ready[^\n]*\|\|/);
  const rollbackTrapAt = source.indexOf("trap rollback ERR");
  const backendStartAt = source.lastIndexOf('systemctl start "grandumi-production-backend@$target.service"');
  const waitCallAt = source.lastIndexOf("wait_for_backend_ready");
  const proxyAt = source.indexOf("write_proxy", waitCallAt);
  assert.ok(
    rollbackTrapAt >= 0
      && backendStartAt > rollbackTrapAt
      && waitCallAt > backendStartAt
      && proxyAt > waitCallAt,
    "目标后端必须在回退 trap 生效后启动，且只有有界等待成功后才切换代理。 ",
  );

  const directory = await mkdtemp(path.join(tempRoot, "backend-ready-test-"));
  const bashDirectory = resolveBashPath(bash, directory);
  const runScenario = (readyAfter, serviceState, changedPid = false, wrongVersion = false) => spawnSync(
    bash,
    [
      "-c",
      `${waitFunction}\n`
        + 'attempt_file="$1/attempts"\n'
        + 'printf 0 > "$attempt_file"\n'
        + "test_seconds=0\n"
        + "backend_ready_timeout_seconds=600\n"
        + "backend_progress_interval_seconds=10\n"
        + 'target_commit="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"\n'
        + `ready_after=${readyAfter}\n`
        + `service_state=${JSON.stringify(serviceState)}\n`
        + `changed_pid=${changedPid ? 1 : 0}\nwrong_version=${wrongVersion ? 1 : 0}\n`
        + 'date() { printf "%s\\n" "$test_seconds"; }\n'
        + 'curl() { if [[ "$*" == */version ]]; then if (( wrong_version )); then printf \'{"commit":"wrong"}\'; else printf \'{"commit":"%s"}\' "$target_commit"; fi; return; fi; '
        + 'attempts=$(< "$attempt_file"); attempts=$((attempts + 1)); printf "%s" "$attempts" > "$attempt_file"; '
        + '(( attempts >= ready_after )) || return 1; printf \'{"status":"ready","storage":{"healthy":true},"recovery":{}}\'; }\n'
        + 'systemctl() { if [[ "$1" == show ]]; then if (( changed_pid && test_seconds > 0 )); then printf 456; else printf 123; fi; else [[ "$service_state" == active ]]; fi; }\n'
        + 'journalctl() { :; }\n'
        + `clock_step=${readyAfter === 999 && serviceState === "active" ? 100 : 1}\n`
        + "sleep() { test_seconds=$((test_seconds + $1 * clock_step)); }\n"
        + (process.platform === "win32" ? 'python3() { py -3 "$@"; }\n' : "")
        + "set -Ee\n"
        + 'trap \'status=$?; printf "status=%s attempts=%s seconds=%s\\n" "$status" "$(< "$attempt_file")" "$test_seconds"; exit 0\' ERR\n'
        + 'wait_for_backend_ready b 8082 "$target_commit"\n'
        + "status=$?\n"
        + 'printf \'status=%s attempts=%s seconds=%s\\n\' "$status" "$(< "$attempt_file")" "$test_seconds"\n',
      "backend-ready-test",
      bashDirectory,
    ],
    { encoding: "utf8" },
  );

  try {
    const slowRecovery = runScenario(40, "active");
    assert.equal(slowRecovery.status, 0, slowRecovery.stderr || slowRecovery.stdout);
    assert.match(slowRecovery.stdout, /status=0 attempts=40 seconds=39/);

    const crashed = runScenario(999, "inactive");
    assert.equal(crashed.status, 0, crashed.stderr || crashed.stdout);
    assert.match(crashed.stdout, /status=1 attempts=0 seconds=0/);

    const timedOut = runScenario(999, "active");
    assert.equal(timedOut.status, 0, timedOut.stderr || timedOut.stdout);
    assert.match(timedOut.stdout, /status=1 attempts=6 seconds=600/);
    assert.match(runScenario(40, "active", true).stdout, /status=1 attempts=1 seconds=1/);
    assert.match(runScenario(1, "active", false, true).stdout, /status=1 attempts=1 seconds=0/);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("正式发布只为规则语义兼容的旧内置版本生成恢复别名，并在停旧进程前复核清单", async () => {
  const stage = await readFile(
    path.join(root, "ops", "server", "stage-grandumi-production.sh"),
    "utf8",
  );
  const compatibility = await readFile(
    path.join(root, "ops", "server", "grandumi-builtin-recovery-compat.sh"),
    "utf8",
  );
  const aliasBuilder = await readFile(
    path.join(root, "ops", "server", "build-grandumi-builtin-recovery-alias-manifest.sh"),
    "utf8",
  );
  const switching = await readFile(
    path.join(root, "ops", "server", "grandumi-production-switch.sh"),
    "utf8",
  );
  const manager = await readFile(
    path.join(root, "服务端WebSocket", "Effects", "Rules", "CardRuleset.cs"),
    "utf8",
  );
  const accountOccupancy = await readFile(
    path.join(root, "服务端WebSocket", "Game", "AccountRoomOccupancy.cs"),
    "utf8",
  );
  const program = await readFile(path.join(root, "服务端WebSocket", "Program.cs"), "utf8");

  assert.match(stage, /build_builtin_recovery_alias_manifest/);
  assert.match(stage, /GRANDUMI_PRODUCTION_DRAINED/);
  assert.match(stage, /grandumi_verify_production_drained_state/);
  assert.match(stage, /include_deployed=0/);
  assert.match(stage, /\.grandumi-production-drained-release-v1/);
  assert.match(aliasBuilder, /\/var\/lib\/grandumi-production-deployed/);
  assert.match(aliasBuilder, /\/data\/grandumi\/Persist/);
  assert.match(aliasBuilder, /header\.get\("rulesetId"\)/);
  assert.match(aliasBuilder, /merge-base --is-ancestor "\$commit" "\$target"/);
  assert.match(aliasBuilder, /diff --name-only -z[\s\\]+\n\s+"\$commit" "\$target" -- 服务端WebSocket 卡牌数据/);
  assert.match(aliasBuilder, /source "\$source_root\/ops\/server\/grandumi-builtin-recovery-compat\.sh"/);
  assert.match(aliasBuilder, /grandumi_is_builtin_recovery_compatible_change/);
  assert.match(aliasBuilder, /未授权服务端\/卡表差异/);
  assert.match(aliasBuilder, /grandumi\.builtin-ruleset-recovery-aliases\.v1/);
  assert.match(aliasBuilder, /"targetRulesetId": target/);
  assert.match(aliasBuilder, /"aliases": aliases/);
  assert.match(compatibility, /服务端WebSocket\/Effects\/Rules\/CardRuleset\.cs/);
  assert.match(compatibility, /服务端WebSocket\/Persistence\/QqAccessStore\.cs/);
  assert.match(compatibility, /52a9dbedc7bd6150e85cb8f50636bc31488f5840/);
  assert.match(compatibility, /f39ab1998cbfcbb2c2eeea4c30060f48e7b80bb0/);
  assert.match(compatibility, /服务端WebSocket\/QqWhitelistSyncHttpEndpoint\.cs/);
  assert.match(compatibility, /46511a0350b79a99652ac4d14ea7102c2efbfee4/);
  assert.match(compatibility, /bbc934908197dc86538f1f47586e3a83bc85d038/);
  assert.match(compatibility, /服务端WebSocket\/Game\/AccountRoomOccupancy\.cs/);
  assert.match(compatibility, /43af6b1c07a8a9ef5fb33c9805436877ca4a62ef/);
  assert.match(compatibility, /服务端WebSocket\/WebSocketBridge\.cs/);
  assert.match(compatibility, /d85b8dfbc7b231db8ed5450a9350c76fcf902292/);
  assert.match(compatibility, /90cbd522721a7a38ae629f2b586c1a0c11275fb3/);
  assert.match(accountOccupancy, /internal enum AccountRoomOccupancyKind/);
  assert.match(accountOccupancy, /internal readonly record struct AccountRoomOccupancy/);
  assert.match(accountOccupancy, /internal sealed class AccountRoomOccupiedException/);
  assert.doesNotMatch(
    accountOccupancy,
    /GameEngine|GameState|MatchReplay|RoomJournal|RoomRecoverySnapshotStore|CloudReplay|CardDatabase|CardRuleset/,
    "账号占用结构不得依赖卡牌、引擎状态、动作日志、快照或恢复实现。",
  );

  const verifyAt = switching.indexOf("verify_ruleset_recovery_alias_manifest");
  const stopOldAt = switching.indexOf('systemctl stop "grandumi-production-backend@$active.service"');
  assert.ok(verifyAt >= 0 && stopOldAt > verifyAt, "目标清单必须在停止旧单写者之前验证。 ");
  assert.match(switching, /document\.get\("targetRulesetId"\) != sys\.argv\[2\]/);
  assert.match(switching, /len\(aliases\) > 32/);
  assert.match(switching, /verify_drained_release_contract/);
  assert.match(switching, /grandumi\.production-drained-release\.v1/);
  assert.match(switching, /verify_empty_persist_after_old_backend_stop/);

  assert.match(manager, /BuiltInRecoveryAliases = new\(StringComparer\.Ordinal\)/);
  assert.match(manager, /BuiltInRecoveryAliases\.TryGetValue\(rulesetId/);
  assert.match(manager, /Rulesets\.TryGetValue\(rulesetId/);
  assert.doesNotMatch(manager, /Rulesets\[alias\]/);
  const aliasInitAt = program.indexOf("InitializeBuiltInRecoveryAliases");
  const packageInitAt = program.indexOf("InitializePackages", aliasInitAt);
  assert.ok(aliasInitAt >= 0 && packageInitAt > aliasInitAt,
    "恢复别名必须在持久规则包和房间恢复前加载。 ");
});

test("排空发布仅在无活动日志时省略旧版本别名，出现旧日志仍执行原兼容门禁", async () => {
  const bash = resolveBash();
  const helper = resolveBashPath(
    bash,
    path.join(root, "ops", "server", "build-grandumi-builtin-recovery-alias-manifest.sh"),
  );
  const directory = await mkdtemp(path.join(tempRoot, "drained-alias-test-"));
  try {
    git(["init", "--quiet"], directory);
    git(["config", "user.name", "GrandUMI Deploy Test"], directory);
    git(["config", "user.email", "deploy-test@grand-umi.invalid"], directory);
    git(["config", "core.autocrlf", "false"], directory);
    git(["config", "commit.gpgsign", "false"], directory);
    await writeTrackedFile(directory, "卡牌数据/cards.json", "旧卡表\n");
    git(["add", "--all"], directory);
    git(["commit", "--quiet", "-m", "old rules"], directory);
    const oldCommit = git(["rev-parse", "HEAD"], directory);
    await writeTrackedFile(directory, "卡牌数据/cards.json", "新卡表\n");
    git(["add", "--all"], directory);
    git(["commit", "--quiet", "-m", "new rules"], directory);
    const target = git(["rev-parse", "HEAD"], directory);

    const publish = path.join(directory, "publish");
    const persist = path.join(directory, "Persist");
    const deployed = path.join(directory, "deployed");
    await mkdir(publish, { recursive: true });
    await mkdir(persist, { recursive: true });
    await writeFile(deployed, `${oldCommit}\n`, "utf8");
    const args = [
      helper,
      resolveBashPath(bash, directory),
      target,
      resolveBashPath(bash, publish),
      "0",
      resolveBashPath(bash, deployed),
      resolveBashPath(bash, persist),
    ];
    const drained = spawnSync(bash, args, { encoding: "utf8" });
    assert.equal(drained.status, 0, drained.stderr || drained.stdout);
    const manifest = JSON.parse(await readFile(path.join(publish, "builtin-ruleset-recovery-aliases.json"), "utf8"));
    assert.deepEqual(manifest.aliases, [], "已排空且没有日志时不应强制映射旧规则版本。 ");

    await rm(publish, { recursive: true, force: true });
    await mkdir(publish, { recursive: true });
    const defaultMode = spawnSync(bash, args.map((value, index) => index === 4 ? "1" : value), { encoding: "utf8" });
    assert.notEqual(defaultMode.status, 0, "未进入排空模式时必须继续校验当前正式版本。 ");
    assert.match(defaultMode.stderr, /未授权服务端\/卡表差异/);

    await rm(publish, { recursive: true, force: true });
    await mkdir(publish, { recursive: true });
    await writeFile(
      path.join(persist, "abcdef123456.jsonl"),
      `${JSON.stringify({ kind: "create", rulesetId: `builtin-${oldCommit}` })}\n`,
      "utf8",
    );
    const withJournal = spawnSync(bash, args, { encoding: "utf8" });
    assert.notEqual(withJournal.status, 0, "排空模式发现旧日志时仍必须执行兼容门禁。 ");
    assert.match(withJournal.stderr, /未授权服务端\/卡表差异/);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("排空状态门禁忽略归档云回放计数，并拒绝维护关闭、活动房间、恢复队列或日志", async () => {
  const bash = resolveBash();
  const helper = resolveBashPath(
    bash,
    path.join(root, "ops", "server", "grandumi-production-drained-state.sh"),
  );
  const directory = await mkdtemp(path.join(tempRoot, "drained-state-test-"));
  try {
    const readyPath = path.join(directory, "ready.json");
    const persist = path.join(directory, "Persist");
    await mkdir(persist, { recursive: true });
    const base = {
      status: "ready",
      storage: { healthy: true },
      maintenance: true,
      rooms: 0,
      recovery: {
        pausedRooms: 0,
        journalQueueDepth: 0,
        snapshotQueueDepth: 0,
        cloudReplayPendingCompletions: 185,
        cloudReplayIsolatedFailures: 185,
      },
    };
    const run = () => spawnSync(bash, [
      helper,
      "--snapshot",
      resolveBashPath(bash, readyPath),
      resolveBashPath(bash, persist),
      "测试排空门禁",
    ], { encoding: "utf8" });

    await writeFile(readyPath, JSON.stringify(base), "utf8");
    assert.equal(run().status, 0, "云回放归档计数不属于活动对局，不应阻止发布。 ");

    for (const invalid of [
      { ...base, maintenance: false },
      { ...base, rooms: 1 },
      { ...base, recovery: { ...base.recovery, pausedRooms: 1 } },
      { ...base, recovery: { ...base.recovery, journalQueueDepth: 1 } },
      { ...base, recovery: { ...base.recovery, snapshotQueueDepth: 1 } },
    ]) {
      await writeFile(readyPath, JSON.stringify(invalid), "utf8");
      assert.notEqual(run().status, 0, "任何未排空状态都必须失败关闭。 ");
    }

    await writeFile(readyPath, JSON.stringify(base), "utf8");
    await writeFile(path.join(persist, "abcdef123456.jsonl"), "{}\n", "utf8");
    assert.notEqual(run().status, 0, "活动日志存在时必须失败关闭。 ");
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("旧后端停写后出现日志会触发回退，且不会改写代理", async () => {
  const bash = resolveBash();
  const switching = await readFile(
    path.join(root, "ops", "server", "grandumi-production-switch.sh"),
    "utf8",
  );
  const functionStart = switching.indexOf("verify_empty_persist_after_old_backend_stop() {");
  const functionEnd = switching.indexOf("\nprotect_rollback_incompatible_recovery()", functionStart);
  assert.ok(functionStart >= 0 && functionEnd > functionStart,
    "切槽脚本必须在旧后端停写后复核活动日志。 ");
  const directory = await mkdtemp(path.join(tempRoot, "drained-cutover-test-"));
  try {
    const persist = path.join(directory, "Persist");
    await mkdir(persist, { recursive: true });
    await writeFile(path.join(persist, "abcdef123456.jsonl"), "{}\n", "utf8");
    const bashPersist = resolveBashPath(bash, persist).replaceAll("\\", "\\\\").replaceAll('"', '\\"');
    const testedFunction = switching.slice(functionStart, functionEnd)
      .replaceAll("/data/grandumi/Persist", bashPersist);
    const behavior = spawnSync(
      bash,
      [
        "-c",
        [
          "set -Eeuo pipefail",
          testedFunction,
          "active=a",
          'marker_root="$1"',
          'systemctl() { return 3; }',
          'rollback() { touch "$marker_root/rollback-called"; }',
          'write_proxy() { touch "$marker_root/proxy-called"; }',
          "trap rollback ERR",
          "verify_empty_persist_after_old_backend_stop",
          "write_proxy 8080 3000 a",
        ].join("\n"),
        "drained-cutover-test",
        resolveBashPath(bash, directory),
      ],
      { encoding: "utf8" },
    );
    assert.notEqual(behavior.status, 0, "停写后日志复核失败必须进入 ERR 回退路径。 ");
    await readFile(path.join(directory, "rollback-called"), "utf8");
    await assert.rejects(readFile(path.join(directory, "proxy-called"), "utf8"), /ENOENT/);
    const stopAt = switching.indexOf('systemctl stop "grandumi-production-backend@$active.service"');
    const postStopAt = switching.indexOf("verify_empty_persist_after_old_backend_stop", stopAt);
    const newBackendAt = switching.indexOf('systemctl start "grandumi-production-backend@$target.service"', postStopAt);
    assert.ok(stopAt >= 0 && postStopAt > stopAt && newBackendAt > postStopAt,
      "空日志复核必须位于旧后端停写之后、新后端启动之前。 ");
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("恢复别名门禁只放行已审计 QQ 与账号占用边界 blob，并拒绝其他变化", async () => {
  const bash = resolveBash();
  const helperPath = resolveBashPath(
    bash,
    path.join(root, "ops", "server", "grandumi-builtin-recovery-compat.sh"),
  );
  const directory = await mkdtemp(path.join(tempRoot, "recovery-compat-test-"));
  const bashRepository = resolveBashPath(bash, directory);
  const qqPath = "服务端WebSocket/Persistence/QqAccessStore.cs";
  const qqEndpointPath = "服务端WebSocket/QqWhitelistSyncHttpEndpoint.cs";
  const occupancyPath = "服务端WebSocket/Game/AccountRoomOccupancy.cs";
  const bridgePath = "服务端WebSocket/WebSocketBridge.cs";
  const legacyCommit = "28e680a7bee6b8ae99ed401651a9f6e3e09c9f8a";
  const auditedCommit = "c91a56baa289a5116390f75da25ffc6b03bea152";
  const occupancyAuditCommit = "045d3debc8da3aa919bd62150dee648333c63f84";
  const legacyQq = spawnSync("git", ["show", `${legacyCommit}:${qqPath}`], { cwd: root });
  const auditedQq = spawnSync("git", ["show", `${auditedCommit}:${qqPath}`], { cwd: root });
  const legacyQqEndpoint = spawnSync(
    "git", ["show", `${legacyCommit}:${qqEndpointPath}`], { cwd: root },
  );
  const auditedQqEndpoint = spawnSync(
    "git", ["show", `${auditedCommit}:${qqEndpointPath}`], { cwd: root },
  );
  const auditedOccupancy = spawnSync(
    "git", ["show", `${occupancyAuditCommit}:${occupancyPath}`], { cwd: root },
  );
  const legacyBridge = spawnSync(
    "git", ["show", `b56fa0ec1c7085aa0ae26dd1925311493bf40a01:${bridgePath}`], { cwd: root },
  );
  const auditedBridge = spawnSync(
    "git", ["show", `${occupancyAuditCommit}:${bridgePath}`], { cwd: root },
  );
  assert.equal(legacyQq.status, 0, legacyQq.stderr?.toString());
  assert.equal(auditedQq.status, 0, auditedQq.stderr?.toString());
  assert.equal(legacyQqEndpoint.status, 0, legacyQqEndpoint.stderr?.toString());
  assert.equal(auditedQqEndpoint.status, 0, auditedQqEndpoint.stderr?.toString());
  assert.equal(auditedOccupancy.status, 0, auditedOccupancy.stderr?.toString());
  assert.equal(legacyBridge.status, 0, legacyBridge.stderr?.toString());
  assert.equal(auditedBridge.status, 0, auditedBridge.stderr?.toString());

  try {
    git(["init", "--quiet"], directory);
    git(["config", "user.name", "GrandUMI Recovery Gate Test"], directory);
    git(["config", "user.email", "recovery-gate@grand-umi.invalid"], directory);
    git(["config", "core.autocrlf", "false"], directory);
    git(["config", "commit.gpgsign", "false"], directory);

    await writeTrackedFile(directory, "卡牌数据/cards.json", "旧卡牌数据\n");
    await writeTrackedFile(directory, "服务端WebSocket/Effects/Scripted/TestCard.cs", "旧卡效\n");
    await writeTrackedFile(directory, "服务端WebSocket/Game/GameState.cs", "旧对局状态\n");
    const qqDestination = path.join(directory, ...qqPath.split("/"));
    const qqEndpointDestination = path.join(directory, ...qqEndpointPath.split("/"));
    const occupancyDestination = path.join(directory, ...occupancyPath.split("/"));
    const bridgeDestination = path.join(directory, ...bridgePath.split("/"));
    await mkdir(path.dirname(qqDestination), { recursive: true });
    await writeFile(qqDestination, legacyQq.stdout);
    await writeFile(qqEndpointDestination, legacyQqEndpoint.stdout);
    await writeFile(bridgeDestination, legacyBridge.stdout);
    git(["add", "--all"], directory);
    git(["commit", "--quiet", "-m", "legacy"], directory);
    const legacy = git(["rev-parse", "HEAD"], directory);

    await writeFile(qqDestination, auditedQq.stdout);
    await writeFile(qqEndpointDestination, auditedQqEndpoint.stdout);
    await writeFile(occupancyDestination, auditedOccupancy.stdout);
    await writeFile(bridgeDestination, auditedBridge.stdout);
    git(["add", "--all"], directory);
    git(["commit", "--quiet", "-m", "audited qq sync"], directory);
    const audited = git(["rev-parse", "HEAD"], directory);
    const accepted = checkRecoveryCompatibility(
      bash, helperPath, bashRepository, legacy, audited, qqPath,
    );
    assert.equal(
      accepted.status,
      0,
      `精确审计的 QQ 持久化转换应通过：${accepted.stderr || accepted.stdout}`,
    );
    const acceptedEndpoint = checkRecoveryCompatibility(
      bash, helperPath, bashRepository, legacy, audited, qqEndpointPath,
    );
    assert.equal(
      acceptedEndpoint.status,
      0,
      `精确审计的 QQ HTTP 转换应通过：${acceptedEndpoint.stderr || acceptedEndpoint.stdout}`,
    );
    const acceptedOccupancy = checkRecoveryCompatibility(
      bash, helperPath, bashRepository, legacy, audited, occupancyPath,
    );
    assert.equal(
      acceptedOccupancy.status,
      0,
      `精确审计的账号占用结构新增应通过：${acceptedOccupancy.stderr || acceptedOccupancy.stdout}`,
    );
    const acceptedBridge = checkRecoveryCompatibility(
      bash, helperPath, bashRepository, legacy, audited, bridgePath,
    );
    assert.equal(
      acceptedBridge.status,
      0,
      `精确审计的建局提示映射转换应通过：${acceptedBridge.stderr || acceptedBridge.stdout}`,
    );

    await writeFile(
      occupancyDestination,
      Buffer.concat([
        auditedOccupancy.stdout,
        Buffer.from("// 未审计的账号占用结构后续改动\n", "utf8"),
      ]),
    );
    git(["add", "--all"], directory);
    git(["commit", "--quiet", "-m", "tampered account occupancy"], directory);
    const tamperedOccupancy = git(["rev-parse", "HEAD"], directory);
    const rejectedOccupancy = checkRecoveryCompatibility(
      bash, helperPath, bashRepository, legacy, tamperedOccupancy, occupancyPath,
    );
    assert.notEqual(
      rejectedOccupancy.status,
      0,
      "AccountRoomOccupancy 任意额外变化都必须失败关闭。",
    );

    await writeFile(
      bridgeDestination,
      Buffer.concat([
        auditedBridge.stdout,
        Buffer.from("// 未审计的建局桥接层后续改动\n", "utf8"),
      ]),
    );
    git(["add", "--all"], directory);
    git(["commit", "--quiet", "-m", "tampered account occupancy bridge"], directory);
    const tamperedBridge = git(["rev-parse", "HEAD"], directory);
    const rejectedBridge = checkRecoveryCompatibility(
      bash, helperPath, bashRepository, legacy, tamperedBridge, bridgePath,
    );
    assert.notEqual(
      rejectedBridge.status,
      0,
      "WebSocketBridge 任意额外变化都必须失败关闭。",
    );

    await writeFile(
      qqDestination,
      Buffer.concat([auditedQq.stdout, Buffer.from("// 未审计的后续改动\n", "utf8")]),
    );
    git(["add", "--all"], directory);
    git(["commit", "--quiet", "-m", "tampered qq store"], directory);
    const tamperedQq = git(["rev-parse", "HEAD"], directory);
    const rejectedQq = checkRecoveryCompatibility(
      bash, helperPath, bashRepository, legacy, tamperedQq, qqPath,
    );
    assert.notEqual(rejectedQq.status, 0, "QqAccessStore 任意额外变化都必须失败关闭。");

    await writeFile(
      qqEndpointDestination,
      Buffer.concat([
        auditedQqEndpoint.stdout,
        Buffer.from("// 未审计的 HTTP 端点后续改动\n", "utf8"),
      ]),
    );
    git(["add", "--all"], directory);
    git(["commit", "--quiet", "-m", "tampered qq endpoint"], directory);
    const tamperedEndpoint = git(["rev-parse", "HEAD"], directory);
    const rejectedEndpoint = checkRecoveryCompatibility(
      bash, helperPath, bashRepository, legacy, tamperedEndpoint, qqEndpointPath,
    );
    assert.notEqual(
      rejectedEndpoint.status,
      0,
      "QqWhitelistSyncHttpEndpoint 任意额外变化都必须失败关闭。",
    );

    await writeTrackedFile(directory, "卡牌数据/cards.json", "篡改卡牌数据\n");
    git(["add", "--all"], directory);
    git(["commit", "--quiet", "-m", "changed card data"], directory);
    const changedCardData = git(["rev-parse", "HEAD"], directory);
    const rejectedCardData = checkRecoveryCompatibility(
      bash, helperPath, bashRepository, tamperedEndpoint, changedCardData, "卡牌数据/cards.json",
    );
    assert.notEqual(rejectedCardData.status, 0, "卡牌数据变化必须失败关闭。");

    await writeTrackedFile(
      directory,
      "服务端WebSocket/Effects/Scripted/TestCard.cs",
      "篡改卡牌效果\n",
    );
    git(["add", "--all"], directory);
    git(["commit", "--quiet", "-m", "changed card effect"], directory);
    const changedCardEffect = git(["rev-parse", "HEAD"], directory);
    const rejectedCardEffect = checkRecoveryCompatibility(
      bash,
      helperPath,
      bashRepository,
      changedCardData,
      changedCardEffect,
      "服务端WebSocket/Effects/Scripted/TestCard.cs",
    );
    assert.notEqual(rejectedCardEffect.status, 0, "卡牌效果变化必须失败关闭。");

    await writeTrackedFile(directory, "服务端WebSocket/Game/GameState.cs", "篡改对局状态\n");
    git(["add", "--all"], directory);
    git(["commit", "--quiet", "-m", "changed game state"], directory);
    const changedGameState = git(["rev-parse", "HEAD"], directory);
    const rejectedGameState = checkRecoveryCompatibility(
      bash,
      helperPath,
      bashRepository,
      changedCardEffect,
      changedGameState,
      "服务端WebSocket/Game/GameState.cs",
    );
    assert.notEqual(rejectedGameState.status, 0, "对局状态变化必须失败关闭。");
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("正式发布链修复已完整进入 2026.09.02.2 更新日志并保留归档记录", async () => {
  const changelog = await readFile(path.join(root, "opcgpro-web", "src", "data", "changelog.ts"), "utf8");
  const archived = await readFile(
    path.join(
      root,
      "changelog-cache",
      "published",
      "2026.09.02.2",
      "2026-09-02-production-emergency-ab-release.md",
    ),
    "utf8",
  );
  const currentAt = changelog.indexOf('version: "2026.09.02.2"');
  const previousAt = changelog.indexOf('version: "2026.09.02.1"');

  assert.ok(currentAt >= 0 && previousAt > currentAt, "新发布日志必须排在海克斯版本之前。 ");
  assert.match(changelog, /id: "2026-09-02-production-emergency-ab-release"/);
  assert.match(changelog, /修复正式服紧急更新入口失效的问题/);
  assert.match(changelog, /可以不等待在线房间清空/);
  assert.match(archived, /状态：已完成/);
  assert.match(archived, /deploy-grandumi-production-emergency\.sh/);
  assert.match(archived, /--preflight/);
});

test("服务器在任何构建或服务切换前校验提交、tree、策略与文件摘要", async () => {
  const source = await readFile(path.join(root, "ops", "server", "deploy-test.sh"), "utf8");
  const proofAt = source.indexOf('verification-proof.mjs" verify');
  const backendBuildAt = source.indexOf("dotnet publish");
  const frontendBuildAt = source.indexOf("npm run build");
  assert.ok(proofAt >= 0, "服务器缺少验证证明校验。 ");
  assert.ok(proofAt < backendBuildAt && proofAt < frontendBuildAt, "证明校验必须先于所有构建。 ");
  assert.match(source, /--commit "\$target"/);
  assert.match(source, /--tree "\$target_tree"/);
  assert.match(source, /--checksum "\$verification_checksum"/);
  assert.match(source, /test-verified\.json/);
});

test("测试服后端编译关闭共享编译，并阻止编译子进程继承两把部署锁", async () => {
  const source = await readFile(path.join(root, "ops", "server", "deploy-test.sh"), "utf8");
  const publish = source.match(
    /\/opt\/dotnet\/dotnet publish[\s\S]*?(?=\n  \[\[ -f "\$next_publish\/\.grandumi-shared-account-v1")/,
  )?.[0];
  assert.ok(publish, "无法定位测试服后端 publish 命令。");
  assert.match(publish, /-p:UseSharedCompilation=false/);
  assert.match(publish, /8>&- 9>&-\s*$/);

  const bash = resolveBash();
  const behavior = spawnSync(
    bash,
    [
      "-c",
      [
        "set -Eeuo pipefail",
        "exec 8>/dev/null",
        "exec 9>/dev/null",
        "test -e /proc/$$/fd/8",
        "test -e /proc/$$/fd/9",
        `"$1" -c 'test ! -e /proc/$$/fd/8 && test ! -e /proc/$$/fd/9' 8>&- 9>&-`,
        "test -e /proc/$$/fd/8",
        "test -e /proc/$$/fd/9",
      ].join("\n"),
      "grandumi-lock-fd-test",
      bash,
    ],
    { encoding: "utf8" },
  );
  assert.equal(
    behavior.status,
    0,
    `锁 FD 隔离行为不符合预期：${behavior.stderr || behavior.stdout}`,
  );
});

test("门禁失败只前移仓库 HEAD 时，重试仍按最后成功部署版本补齐前后端", async () => {
  const source = await readFile(path.join(root, "ops", "server", "deploy-test.sh"), "utf8");
  assert.match(source, /deployment_state="\$state_dir\/test-deployed"/);
  assert.match(source, /diff --name-only "\$deployment_base" "\$target"/);
  assert.doesNotMatch(source, /diff --name-only "\$repo_head" "\$target"/);
  assert.match(source, /merge-base --is-ancestor "\$deployment_base" "\$target"/);
  assert.match(source, /require_full_deploy "缺少 test-deployed 成功状态"/);
  assert.match(source, /require_full_deploy "无法读取 test-deployed 成功状态"/);
  assert.match(source, /require_full_deploy "test-deployed 成功状态格式非法"/);
  assert.match(source, /require_full_deploy "test-deployed 提交对象不可用"/);
  assert.match(source, /require_full_deploy "test-deployed 不是待部署提交的祖先"/);
  assert.match(source, /require_full_deploy "无法比较 test-deployed 与待部署提交"/);
  assert.match(source, /if \[\[ "\$full_deploy" == 1 \]\]; then\s+need_back=1\s+need_front=1\s+need_npm=1/);
  assert.match(source, /flock -n 9/);

  const directory = await mkdtemp(path.join(tempRoot, "deploy-baseline-test-"));
  try {
    git(["init", "--quiet"], directory);
    git(["config", "user.name", "GrandUMI Deploy Test"], directory);
    git(["config", "user.email", "deploy-test@grand-umi.invalid"], directory);
    git(["config", "core.autocrlf", "false"], directory);
    git(["config", "commit.gpgsign", "false"], directory);

    await writeTrackedFile(directory, "服务端WebSocket/Game/GameEngine.cs", "deployed backend\n");
    await writeTrackedFile(directory, "opcgpro-web/src/app.ts", "deployed frontend\n");
    await writeTrackedFile(directory, "tools/verification-proof.mjs", "deployed gate\n");
    git(["add", "--all"], directory);
    git(["commit", "--quiet", "-m", "deployed"], directory);
    const deployed = git(["rev-parse", "HEAD"], directory);

    await writeTrackedFile(directory, "服务端WebSocket/Game/GameEngine.cs", "failed target backend\n");
    await writeTrackedFile(directory, "opcgpro-web/src/app.ts", "failed target frontend\n");
    git(["add", "--all"], directory);
    git(["commit", "--quiet", "-m", "failed target"], directory);
    const failedHead = git(["rev-parse", "HEAD"], directory);

    await writeTrackedFile(directory, "tools/verification-proof.mjs", "retry gate fix\n");
    git(["add", "--all"], directory);
    git(["commit", "--quiet", "-m", "retry target"], directory);
    const retryTarget = git(["rev-parse", "HEAD"], directory);
    git(["checkout", "--quiet", "--detach", failedHead], directory);

    const staleHeadChanges = git(["-c", "core.quotepath=false", "diff", "--name-only", failedHead, retryTarget], directory)
      .split("\n").filter(Boolean);
    assert.deepEqual(staleHeadChanges, ["tools/verification-proof.mjs"]);

    const deployedChanges = git(["-c", "core.quotepath=false", "diff", "--name-only", deployed, retryTarget], directory)
      .split("\n").filter(Boolean);
    assert.ok(deployedChanges.some((file) => file.startsWith("服务端WebSocket/")), "必须补建未成功部署的后端变化。");
    assert.ok(deployedChanges.some((file) => file.startsWith("opcgpro-web/")), "必须补建未成功部署的前端变化。");
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("统一验证从锁文件安装依赖、先生成卡牌单包，并在证明前恢复派生文件", async () => {
  const source = await readFile(path.join(root, "verify.ps1"), "utf8");
  const lockAt = source.indexOf("$repositoryLock = Enter-GrandUmiRepositoryLock");
  const installAt = source.indexOf('npm ci --prefix "opcgpro-web"');
  const bundleAt = source.indexOf('npm run build:cards --prefix "opcgpro-web"');
  const frontendTestsAt = source.indexOf('Invoke-VerificationSuite "前端完整单元测试"');
  const restoreAt = source.indexOf("  Restore-CardBundleSnapshot", frontendTestsAt);
  const proofAt = source.indexOf("  if ($ProofPath)", frontendTestsAt);

  assert.ok(lockAt >= 0 && installAt > lockAt, "统一验证必须先持有仓库互斥锁再执行可能写盘的步骤。");
  assert.ok(installAt >= 0 && bundleAt > installAt, "必须先按锁文件安装依赖，再生成派生卡牌单包。");
  assert.ok(frontendTestsAt > bundleAt, "前端测试开始前必须完成依赖安装和卡牌单包生成。");
  assert.ok(restoreAt > frontendTestsAt && proofAt > restoreAt, "生成部署证明前必须恢复派生卡牌单包。");
  assert.match(source, /GRANDUMI_REPOSITORY_VERIFICATION = "1"/);
  assert.match(source, /PYTHONDONTWRITEBYTECODE = "1"/);
  assert.match(source, /repositoryStateBeforeQqTests = Get-RepositoryStateFingerprint/);
  assert.match(source, /repositoryStateAfterQqTests -ne \$repositoryStateBeforeQqTests/);
  assert.match(source, /ls-files --others --exclude-standard -z/);
  assert.match(source, /git hash-object --no-filters/);
  assert.match(source, /finally \{[\s\S]*Exit-GrandUmiRepositoryLock/);
  assert.match(source, /finally \{[\s\S]*Restore-CardBundleSnapshot/);
});
