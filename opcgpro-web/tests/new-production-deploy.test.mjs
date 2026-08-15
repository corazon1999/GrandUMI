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
const directTls = await readFile(new URL("../../ops/server/enable-grandumi-production-direct-tls.sh", import.meta.url), "utf8");
const emergencyDirectRelay = await readFile(new URL("../../ops/server/grandumi-emergency-direct-relay.caddy", import.meta.url), "utf8");
const enableEmergencyDirectRelay = await readFile(new URL("../../ops/server/enable-grandumi-emergency-direct-relay.sh", import.meta.url), "utf8");

test("新正式服预构建固定使用正式 HTTPS/WSS 域名", () => {
  assert.match(stage, /NEXT_PUBLIC_WS_URL='wss:\/\/grand-umi\.com\/ws'/);
  assert.match(stage, /NEXT_PUBLIC_ASSET_ORIGIN='https:\/\/grand-umi\.com'/);
  assert.match(stage, /"hosts":\["grand-umi\.com","direct\.grand-umi\.com"\]/);
  assert.match(stage, /wss:\/\/direct\.grand-umi\.com\/ws/);
  assert.doesNotMatch(stage, /wss:\/\/candidate\.grand-umi\.com\/ws/);
  assert.match(stage, /尚未切换服务/);
});

test("正式服发布槽始终挂载不进入 Git 的共享卡图资源", () => {
  assert.match(stage, /shared_asset_root=\/www/);
  assert.match(stage, /card_asset_dirs=\(cards-thumb cards-webp\)/);
  assert.match(stage, /rsync -a "\$source_dir\/" "\$shared_dir\/"/);
  assert.match(stage, /ln -s "\$shared_asset_root\/\$asset_dir" "\$slot_asset_path"/);
  assert.match(stage, /正式服共享卡图目录为空/);
});

test("正式入口同时承载主域名和独立证书的低延迟直连域名", () => {
  assert.match(nginx, /server_name grand-umi\.com;/);
  assert.match(nginx, /live\/grand-umi\.com\/fullchain\.pem/);
  assert.match(nginx, /server_name direct\.grand-umi\.com;/);
  assert.match(nginx, /live\/direct\.grand-umi\.com\/fullchain\.pem/);
  assert.doesNotMatch(nginx, /server_name candidate\.grand-umi\.com/);
  assert.equal((nginx.match(/grandumi-production-proxy\.conf/g) ?? []).length, 2);
  assert.match(candidateNginx, /server_name candidate\.grand-umi\.com;/);
  assert.match(candidateNginx, /live\/candidate\.grand-umi\.com\/fullchain\.pem/);
  assert.doesNotMatch(candidateNginx, /default_server/);
});

test("直连启用前必须完成 DNS 独占、证书主机名和活动槽运行时配置校验", () => {
  assert.match(directTls, /direct\.grand-umi\.com/);
  assert.match(directTls, /resolved_ipv4/);
  assert.match(directTls, /103\.146\.230\.37/);
  assert.match(directTls, /openssl x509[\s\S]*-checkhost/);
  assert.match(directTls, /network-endpoints\.json/);
  assert.match(directTls, /wss:\/\/direct\.grand-umi\.com\/ws/);
  assert.match(directTls, /backend\/ready/);
  assert.match(productionBootstrap, /缺少 direct\.grand-umi\.com 证书/);
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
  assert.match(candidateDeploy, /GRANDUMI_CANDIDATE_ASSET_ORIGIN:-https:\/\/\$candidate_host/);
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
  assert.match(deploy, /worktree add --detach/);
  assert.doesNotMatch(deploy, /checkout --detach/);
  assert.match(deploy, /尚未切流/);
  assert.match(deploy, /Resolve-DnsName -Type A direct\.grand-umi\.com/);
  assert.match(deploy, /低延迟直连 TLS\/健康检查失败/);
});

test("应急直连中转固定进入新正式服并保留自动回滚", () => {
  assert.match(emergencyDirectRelay, /direct\.grand-umi\.com/);
  assert.match(emergencyDirectRelay, /reverse_proxy https:\/\/103\.146\.230\.37/);
  assert.match(emergencyDirectRelay, /header_up Host grand-umi\.com/);
  assert.match(emergencyDirectRelay, /tls_server_name grand-umi\.com/);
  assert.match(enableEmergencyDirectRelay, /direct\.grand-umi\.com\.caddy\.pre-relay-/);
  assert.match(enableEmergencyDirectRelay, /rollback\(\)/);
  assert.match(enableEmergencyDirectRelay, /caddy validate/);
  assert.match(enableEmergencyDirectRelay, /systemctl reload caddy/);
});
