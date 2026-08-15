import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const deployTest = await readFile(new URL("../../deploy-test.ps1", import.meta.url), "utf8");

test("统一部署入口先发布新服务器候选环境，再发布原测试服", () => {
  const candidateDeploy = deployTest.indexOf('deploy-new-hk-candidate.ps1');
  const testDeploy = deployTest.indexOf('bash /opt/grandumi-test/deploy.sh');

  assert.ok(candidateDeploy >= 0, "缺少新服务器候选环境部署步骤");
  assert.ok(testDeploy >= 0, "缺少原测试服部署步骤");
  assert.ok(candidateDeploy < testDeploy, "新服务器候选环境必须先于原测试服部署");
  assert.match(deployTest, /候选服部署失败，已停止后续测试服部署/);
});
