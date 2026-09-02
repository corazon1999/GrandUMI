import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import test from "node:test";

const root = path.resolve(import.meta.dirname, "..");
const tempRoot = process.env.GRANDUMI_TEST_TEMP_ROOT;
if (!tempRoot) throw new Error("部署门禁测试必须设置 GRANDUMI_TEST_TEMP_ROOT。");

function git(args, cwd) {
  const result = spawnSync("git", args, { cwd, encoding: "utf8" });
  assert.equal(result.status, 0, result.stderr);
  return result.stdout.trim();
}

async function writeTrackedFile(repository, relativePath, content) {
  const destination = path.join(repository, relativePath);
  await mkdir(path.dirname(destination), { recursive: true });
  await writeFile(destination, content, "utf8");
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
});

test("正式服紧急入口只发布精确 main，并执行目标提交内的版本化 A/B 脚本", async () => {
  const source = await readFile(path.join(root, "deploy-hk.ps1"), "utf8");
  const pushAt = source.indexOf("& $git push origin main");
  const remoteFetchAt = source.indexOf("git -C /opt/grandumi fetch --force --prune");
  const deployAt = source.indexOf("deploy-grandumi-production-emergency.sh");

  assert.match(source, /\[switch\]\$Emergency/);
  assert.match(source, /if \(-not \$Emergency\)/);
  assert.match(source, /root@103\.146\.230\.37/);
  assert.match(source, /\$Server -ne "root@103\.146\.230\.37"/);
  assert.match(source, /git merge --ff-only refs\/remotes\/origin\/main/);
  assert.match(source, /\$originHead -ne \$localHead/);
  assert.match(source, /ls-tree -r --name-only \$localHead -- changelog-cache\/pending/);
  assert.ok(pushAt >= 0 && remoteFetchAt > pushAt && deployAt > remoteFetchAt,
    "必须先精确推送，再只更新远端 Git ref，最后执行版本化发布脚本。");
  assert.match(source, /git -C \/opt\/grandumi show '\$\{localHead\}:\$serverScriptPath'/);
  assert.match(source, /bash "`\$script" --emergency '\$localHead'/);
  assert.doesNotMatch(source, /git add -A/);
  assert.doesNotMatch(source, /git pull --no-rebase/);
  assert.doesNotMatch(source, /\/opt\/grandumi\/deploy\.sh/);
  assert.doesNotMatch(source, /git[^\n]*(?:checkout|reset --hard)/);
});

test("服务器紧急发布跳过房间排空，但不绕过验证、祖先、账号权威与失败恢复门禁", async () => {
  const source = await readFile(
    path.join(root, "ops", "server", "deploy-grandumi-production-emergency.sh"),
    "utf8",
  );
  const activate = await readFile(
    path.join(root, "ops", "server", "activate-grandumi-production.sh"),
    "utf8",
  );

  assert.match(source, /--emergency\|--preflight/);
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
  assert.match(activate, /grandumi-production-switch --release "\$target"/);
  assert.match(activate, /切换脚本自动回滚/);
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
