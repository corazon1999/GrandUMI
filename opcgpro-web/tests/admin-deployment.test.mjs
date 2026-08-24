import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const [runner, pathUnit, serviceUnit, testBackend, productionBackend, testDeploy, stage, activate, bootstrap, bridge, program] = await Promise.all([
  readFile(new URL("../../ops/server/grandumi-admin-deploy.sh", import.meta.url), "utf8"),
  readFile(new URL("../../ops/server/grandumi-admin-deploy.path", import.meta.url), "utf8"),
  readFile(new URL("../../ops/server/grandumi-admin-deploy.service", import.meta.url), "utf8"),
  readFile(new URL("../../ops/server/grandumi-test-backend.service", import.meta.url), "utf8"),
  readFile(new URL("../../ops/server/grandumi-production-backend@.service", import.meta.url), "utf8"),
  readFile(new URL("../../ops/server/deploy-test.sh", import.meta.url), "utf8"),
  readFile(new URL("../../ops/server/stage-grandumi-production.sh", import.meta.url), "utf8"),
  readFile(new URL("../../ops/server/activate-grandumi-production.sh", import.meta.url), "utf8"),
  readFile(new URL("../../ops/server/bootstrap-grandumi-production.sh", import.meta.url), "utf8"),
  readFile(new URL("../../服务端WebSocket/WebSocketBridge.cs", import.meta.url), "utf8"),
  readFile(new URL("../../服务端WebSocket/Program.cs", import.meta.url), "utf8"),
]);

test("发布协议沿用管理员白名单并由服务端在正式发布前进入维护", () => {
  assert.match(bridge, /case "MsgAdminOperations": OnAdminOperations/);
  assert.match(bridge, /case "MsgAdminDeploy": OnAdminDeploy/);
  assert.match(bridge, /GlobalAnnouncementPolicy\.IsAuthorized\(session\.Account\)/);
  assert.match(bridge, /environment == "production"[\s\S]*GameRoomManager\.SetMaintenanceMode\(true\)/);
  assert.match(program, /maintenance = GameRoomManager\.GetMaintenanceSnapshot\(\)\.Enabled/);
});

test("网页后端只写请求目录，root执行器由systemd path隔离触发", () => {
  assert.match(testBackend, /GRANDUMI_ADMIN_DEPLOY_DIR=\/var\/lib\/grandumi-admin-deploy/);
  assert.match(productionBackend, /GRANDUMI_ADMIN_DEPLOY_DIR=\/var\/lib\/grandumi-admin-deploy/);
  assert.match(testBackend, /ReadWritePaths=.*\/requests/);
  assert.match(productionBackend, /ReadWritePaths=.*\/requests/);
  assert.match(pathUnit, /PathChanged=\/var\/lib\/grandumi-admin-deploy\/requests/);
  assert.match(serviceUnit, /ExecStart=\/usr\/local\/sbin\/grandumi-admin-deploy/);
  assert.doesNotMatch(testBackend, /User=root/);
});

test("发布执行器只接受精确环境并固定从远端main取版本", () => {
  assert.match(runner, /\^\(test\|production\)-\(\[0-9a-f\]\{32\}\)/);
  assert.match(runner, /refs\/heads\/main:refs\/remotes\/admin\/main/);
  assert.match(runner, /deploy_test\(\)/);
  assert.match(runner, /deploy_production\(\)/);
  assert.doesNotMatch(runner, /eval /);
  assert.doesNotMatch(runner, /candidate/);
});

test("正式发布必须经过测试服、更新日志、维护模式和零房间门禁", () => {
  assert.match(runner, /grandumi-test-release\/test-deployed/);
  assert.match(runner, /changelog-cache\/pending/);
  assert.match(runner, /snapshot.*true 0/);
  assert.match(runner, /stage-grandumi-production\.sh/);
  assert.match(runner, /activate-grandumi-production\.sh/);
  assert.match(activate, /grandumi-production-deployed\.next/);
  assert.match(activate, /mv \/var\/lib\/grandumi-production-deployed\.next \/var\/lib\/grandumi-production-deployed/);
});

test("测试和正式部署链都会安装并启用受限执行器", () => {
  for (const source of [testDeploy, stage, bootstrap]) {
    assert.match(source, /grandumi-admin-deploy\.sh/);
    assert.match(source, /grandumi-admin-deploy\.path/);
    assert.match(source, /enable --now[^\n]*grandumi-admin-deploy\.path/);
  }
});
