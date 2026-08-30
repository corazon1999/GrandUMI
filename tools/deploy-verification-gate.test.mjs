import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";

const root = path.resolve(import.meta.dirname, "..");

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

test("统一验证从锁文件安装依赖、先生成卡牌单包，并在证明前恢复派生文件", async () => {
  const source = await readFile(path.join(root, "verify.ps1"), "utf8");
  const installAt = source.indexOf('npm ci --prefix "opcgpro-web"');
  const bundleAt = source.indexOf('npm run build:cards --prefix "opcgpro-web"');
  const frontendTestsAt = source.indexOf('Invoke-VerificationSuite "前端完整单元测试"');
  const restoreAt = source.indexOf("  Restore-CardBundleSnapshot", frontendTestsAt);
  const proofAt = source.indexOf("  if ($ProofPath)", frontendTestsAt);

  assert.ok(installAt >= 0 && bundleAt > installAt, "必须先按锁文件安装依赖，再生成派生卡牌单包。");
  assert.ok(frontendTestsAt > bundleAt, "前端测试开始前必须完成依赖安装和卡牌单包生成。");
  assert.ok(restoreAt > frontendTestsAt && proofAt > restoreAt, "生成部署证明前必须恢复派生卡牌单包。");
  assert.match(source, /GRANDUMI_REPOSITORY_VERIFICATION = "1"/);
  assert.match(source, /PYTHONDONTWRITEBYTECODE = "1"/);
  assert.match(source, /finally \{[\s\S]*Restore-CardBundleSnapshot/);
});
