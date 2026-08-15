import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const stage = await readFile(new URL("../../ops/server/stage-grandumi-production.sh", import.meta.url), "utf8");
const activate = await readFile(new URL("../../ops/server/activate-grandumi-production.sh", import.meta.url), "utf8");
const nginx = await readFile(new URL("../../ops/server/grandumi-production.nginx", import.meta.url), "utf8");
const backendService = await readFile(new URL("../../ops/server/grandumi-production-backend.service", import.meta.url), "utf8");
const candidateNginx = await readFile(new URL("../../ops/server/grandumi-candidate-tls.nginx", import.meta.url), "utf8");
const candidateBackendService = await readFile(new URL("../../ops/server/grandumi-candidate-backend.service", import.meta.url), "utf8");
const candidateFrontendService = await readFile(new URL("../../ops/server/grandumi-candidate-frontend.service", import.meta.url), "utf8");
const candidateBackup = await readFile(new URL("../../ops/server/grandumi-candidate-backup.sh", import.meta.url), "utf8");
const candidateDeploy = await readFile(new URL("../../ops/server/deploy-grandumi-candidate.sh", import.meta.url), "utf8");
const productionBootstrap = await readFile(new URL("../../ops/server/bootstrap-grandumi-production.sh", import.meta.url), "utf8");
const deploy = await readFile(new URL("../../deploy-new-hk-production.ps1", import.meta.url), "utf8");

test("新正式服预构建固定使用正式 HTTPS/WSS 域名", () => {
  assert.match(stage, /NEXT_PUBLIC_WS_URL='wss:\/\/grand-umi\.com\/ws'/);
  assert.match(stage, /NEXT_PUBLIC_ASSET_ORIGIN='https:\/\/grand-umi\.com'/);
  assert.match(stage, /"hosts":\["grand-umi\.com"\]/);
  assert.doesNotMatch(stage, /wss:\/\/candidate\.grand-umi\.com\/ws/);
  assert.match(stage, /尚未切换服务/);
});

test("正式入口只承载主域名，候选域名由隔离站点承载", () => {
  assert.match(nginx, /server_name grand-umi\.com;/);
  assert.match(nginx, /live\/grand-umi\.com\/fullchain\.pem/);
  assert.doesNotMatch(nginx, /server_name candidate\.grand-umi\.com/);
  assert.equal((nginx.match(/grandumi-production-proxy\.conf/g) ?? []).length, 1);
  assert.match(candidateNginx, /server_name candidate\.grand-umi\.com;/);
  assert.match(candidateNginx, /live\/candidate\.grand-umi\.com\/fullchain\.pem/);
  assert.doesNotMatch(candidateNginx, /default_server/);
});

test("候选服使用独立端口、独立数据目录和较低资源上限", () => {
  assert.match(candidateBackendService, /GrandUMIServer\.dll 18080/);
  assert.match(candidateBackendService, /GRANDUMI_DATA_DIR=\/data\/grandumi-candidate/);
  assert.match(candidateBackendService, /MemoryMax=1G/);
  assert.match(candidateFrontendService, /-p 13000/);
  assert.match(candidateNginx, /127\.0\.0\.1:18080\/ws/);
  assert.match(candidateNginx, /127\.0\.0\.1:13000/);
  assert.match(candidateBackup, /data_dir=\/data\/grandumi-candidate/);
  assert.doesNotMatch(candidateBackup, /data_dir=\/data\/grandumi\n/);
  assert.match(candidateDeploy, /GRANDUMI_CANDIDATE_ASSET_ORIGIN:-https:\/\/grand-umi\.com/);
  assert.doesNotMatch(productionBootstrap, /rm -f \/etc\/nginx\/sites-enabled\/grandumi-candidate/);
});

test("正式数据未就绪时拒绝激活，失败时恢复候选服务", () => {
  assert.match(activate, /import_dir=\/data\/grandumi-import\/final/);
  assert.match(activate, /\[\[ -f "\$import_dir\/\.ready" \]\]/);
  assert.match(activate, /PRAGMA integrity_check/);
  assert.match(activate, /rollback\(\)/);
  assert.match(activate, /systemctl start grandumi-candidate-backend\.service grandumi-candidate-frontend\.service/);
  assert.match(backendService, /GRANDUMI_NODE_ID=hk-production-01/);
});

test("正式激活会在数据切换前清理候选服重复站点", () => {
  const removeCandidateSite = activate.indexOf("rm -f /etc/nginx/sites-enabled/grandumi-candidate");
  const stopCandidateService = activate.indexOf("systemctl stop grandumi-candidate-frontend.service");
  assert.ok(removeCandidateSite >= 0);
  assert.ok(stopCandidateService > removeCandidateSite);
  assert.match(activate, /systemctl daemon-reload/);
  assert.match(activate, /nginx -t/);
});

test("Windows 部署入口只允许新正式服 IP 且仅做预构建", () => {
  assert.match(deploy, /root@103\.146\.230\.37/);
  assert.doesNotMatch(deploy, /8\.210\.155\.25/);
  assert.match(deploy, /stage-grandumi-production\.sh/);
  assert.match(deploy, /尚未切流/);
});
