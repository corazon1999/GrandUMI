import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const deployTest = await readFile(new URL("../../deploy-test.ps1", import.meta.url), "utf8");

test("统一部署入口只发布测试服且不再调用候选环境", () => {
  const candidateDeploy = deployTest.indexOf('deploy-new-hk-candidate.ps1');
  const testDeploy = deployTest.indexOf('bash /opt/grandumi-test/ops/server/deploy-test.sh');

  assert.ok(testDeploy >= 0, "缺少原测试服部署步骤");
  assert.equal(candidateDeploy, -1, "统一部署入口不得再调用候选环境");
  assert.doesNotMatch(deployTest, /候选服部署失败|candidate\.grand-umi\.com/);
});
