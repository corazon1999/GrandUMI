import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const stage = await readFile(new URL("../../ops/server/stage-grandumi-production.sh", import.meta.url), "utf8");
const activate = await readFile(new URL("../../ops/server/activate-grandumi-production.sh", import.meta.url), "utf8");
const nginx = await readFile(new URL("../../ops/server/grandumi-production.nginx", import.meta.url), "utf8");
const ygoNginx = await readFile(new URL("../../ops/server/grandumi-production-ygo.nginx", import.meta.url), "utf8");
const ygoAcmeNginx = await readFile(new URL("../../ops/server/grandumi-ygo-acme.nginx", import.meta.url), "utf8");
const ygoPrecutNginx = await readFile(new URL("../../ops/server/grandumi-ygo-precut.nginx", import.meta.url), "utf8");
const backendService = await readFile(new URL("../../ops/server/grandumi-production-backend.service", import.meta.url), "utf8");
const candidateNginx = await readFile(new URL("../../ops/server/grandumi-candidate-tls.nginx", import.meta.url), "utf8");
const candidateBackendService = await readFile(new URL("../../ops/server/grandumi-candidate-backend.service", import.meta.url), "utf8");
const candidateFrontendService = await readFile(new URL("../../ops/server/grandumi-candidate-frontend.service", import.meta.url), "utf8");
const candidateBackup = await readFile(new URL("../../ops/server/grandumi-candidate-backup.sh", import.meta.url), "utf8");
const candidateDeploy = await readFile(new URL("../../ops/server/deploy-grandumi-candidate.sh", import.meta.url), "utf8");
const productionBootstrap = await readFile(new URL("../../ops/server/bootstrap-grandumi-production.sh", import.meta.url), "utf8");
const deploy = await readFile(new URL("../../deploy-new-hk-production.ps1", import.meta.url), "utf8");
const emergencyDeploy = await readFile(new URL("../../deploy-hk.ps1", import.meta.url), "utf8");
const directTls = await readFile(new URL("../../ops/server/enable-grandumi-production-direct-tls.sh", import.meta.url), "utf8");
const directTlsRenewHook = await readFile(new URL("../../ops/server/renew-grandumi-direct-certificate.sh", import.meta.url), "utf8");
const directTlsCompatChain = await readFile(new URL("../../ops/server/isrg-root-x2-cross-signed.pem", import.meta.url), "utf8");
const emergencyDirectRelay = await readFile(new URL("../../ops/server/grandumi-emergency-direct-relay.caddy", import.meta.url), "utf8");
const enableEmergencyDirectRelay = await readFile(new URL("../../ops/server/enable-grandumi-emergency-direct-relay.sh", import.meta.url), "utf8");
const assetsNginx = await readFile(new URL("../../ops/server/grandumi-assets.nginx", import.meta.url), "utf8");
const enableAssets = await readFile(new URL("../../ops/server/enable-grandumi-assets.sh", import.meta.url), "utf8");
const productionSwitch = await readFile(new URL("../../ops/server/grandumi-production-switch.sh", import.meta.url), "utf8");
const prepareYgoTls = await readFile(new URL("../../ops/server/prepare-grandumi-ygo-tls.sh", import.meta.url), "utf8");
const switchPrimaryDomain = await readFile(new URL("../../ops/server/switch-grandumi-primary-domain.sh", import.meta.url), "utf8");
const promoteApproved = await readFile(new URL("../../ops/server/promote-approved.sh", import.meta.url), "utf8");
const bridge = await readFile(new URL("../../服务端WebSocket/WebSocketBridge.cs", import.meta.url), "utf8");

test("新正式服预构建固定使用正式 HTTPS/WSS 域名", () => {
  assert.match(stage, /NEXT_PUBLIC_WS_URL='wss:\/\/ygo\.grand-umi\.com\/ws'/);
  assert.match(stage, /NEXT_PUBLIC_ASSET_ORIGIN='https:\/\/assets\.grand-umi\.com'/);
  assert.match(stage, /"hosts":\["ygo\.grand-umi\.com","direct\.grand-umi\.com"\]/);
  assert.match(stage, /wss:\/\/direct\.grand-umi\.com\/ws/);
  assert.match(stage, /wss:\/\/ygo\.grand-umi\.com\/ws/);
  assert.doesNotMatch(stage, /wss:\/\/candidate\.grand-umi\.com\/ws/);
  assert.match(stage, /尚未切换服务/);
  assert.match(emergencyDeploy, /NEXT_PUBLIC_WS_URL='wss:\/\/ygo\.grand-umi\.com\/ws'/);
  assert.match(promoteApproved, /NEXT_PUBLIC_WS_URL='wss:\/\/ygo\.grand-umi\.com\/ws'/);
  assert.doesNotMatch(
    `${stage}\n${emergencyDeploy}\n${promoteApproved}`,
    /NEXT_PUBLIC_WS_URL='wss:\/\/grand-umi\.com\/ws'/,
  );
  assert.match(bridge, /"ygo\.grand-umi\.com" => "ygo\.grand-umi\.com"/);
});

test("新正式服独立承载静态资源域名并跟随活动槽切换", () => {
  assert.match(assetsNginx, /server_name assets\.grand-umi\.com;/);
  assert.match(assetsNginx, /live\/assets\.grand-umi\.com\/fullchain\.pem/);
  assert.match(assetsNginx, /grandumi-active-frontend-files\.conf/);
  assert.match(assetsNginx, /rewrite \^\/_next\/static\/\(\.\*\)\$ \/\.next\/static\/\$1 break/);
  assert.match(assetsNginx, /grandumi-active-assets\.conf/);
  assert.match(assetsNginx, /grandumi-active-backend\.conf/);
  assert.match(assetsNginx, /\/card-back-images\//);
  assert.match(assetsNginx, /respond 404|return 404/);
  assert.match(productionSwitch, /grandumi-active-frontend-files\.conf/);
  assert.match(productionBootstrap, /enable-grandumi-assets/);
  assert.match(productionBootstrap, /checkhost assets\.grand-umi\.com/);
  assert.match(enableAssets, /certbot certonly --webroot/);
  assert.match(enableAssets, /--deploy-hook "systemctl reload nginx"/);
  assert.match(enableAssets, /sprites-thumb\/CardBack\.webp/);
});

test("正式服发布槽始终挂载不进入 Git 的共享卡图资源", () => {
  assert.match(stage, /shared_asset_root=\/www/);
  assert.match(stage, /card_asset_dirs=\(cards-thumb cards-webp\)/);
  assert.match(stage, /rsync -a "\$source_dir\/" "\$shared_dir\/"/);
  assert.match(stage, /ln -s "\$shared_asset_root\/\$asset_dir" "\$slot_asset_path"/);
  assert.match(stage, /正式服共享卡图目录为空/);
  assert.match(stage, /check-card-image-manifest\.mjs/);
  assert.match(stage, /public\/data\/imageManifest\.json/);
  assert.match(stage, /"\$shared_asset_root"/);
});

test("切换前模板继续承载旧主域，预构建不会提前拒绝现网", () => {
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

test("切换后新主域与直连共享正式代理，旧主域只返回 403", () => {
  assert.match(ygoNginx, /server_name ygo\.grand-umi\.com;/);
  assert.match(ygoNginx, /live\/ygo\.grand-umi\.com\/fullchain\.pem/);
  assert.match(ygoNginx, /server_name direct\.grand-umi\.com;/);
  assert.match(ygoNginx, /live\/direct\.grand-umi\.com\/fullchain\.pem/);
  assert.match(ygoNginx, /server_name grand-umi\.com;[\s\S]*return 403;/);
  assert.equal((ygoNginx.match(/grandumi-production-proxy\.conf/g) ?? []).length, 2);
});

test("新主域证书准备始终保持 503 隔离，不会提前开放正式站点", () => {
  assert.match(ygoAcmeNginx, /server_name ygo\.grand-umi\.com;/);
  assert.match(ygoAcmeNginx, /live\/grand-umi\.com\/fullchain\.pem/);
  assert.match(ygoAcmeNginx, /return 503;/);
  assert.match(ygoPrecutNginx, /live\/ygo\.grand-umi\.com\/fullchain\.pem/);
  assert.match(ygoPrecutNginx, /return 503;/);
  assert.match(prepareYgoTls, /HTTP-01 预检/);
  assert.match(prepareYgoTls, /certbot certonly --webroot/);
  assert.match(prepareYgoTls, /-checkhost "\$domain"/);
  assert.match(prepareYgoTls, /strict_code[\s\S]*503/);
});

test("主域切换只允许停机显式执行，并带并发锁、失败回滚和双槽配置更新", () => {
  assert.match(switchPrimaryDomain, /cutover\|rollback/);
  assert.match(switchPrimaryDomain, /flock -n 9/);
  assert.match(switchPrimaryDomain, /systemctl is-active --quiet "\$unit"/);
  assert.match(switchPrimaryDomain, /请先完成维护排空并停服/);
  assert.match(switchPrimaryDomain, /8080\/8082 仍在监听/);
  assert.match(switchPrimaryDomain, /rollback_failed_switch\(\)/);
  assert.match(switchPrimaryDomain, /trap rollback_failed_switch ERR/);
  assert.match(switchPrimaryDomain, /rollback_failed_switch 130/);
  assert.match(switchPrimaryDomain, /old_code[\s\S]*== 403/);
  assert.match(switchPrimaryDomain, /for slot in a b/);
  assert.match(switchPrimaryDomain, /primary-domain-mode/);
  assert.match(productionBootstrap, /cat "\$domain_mode_file"[\s\S]*echo legacy/);
  assert.match(productionBootstrap, /grandumi-production-ygo\.nginx/);
  assert.doesNotMatch(productionBootstrap, /switch-grandumi-primary-domain[^\n]*cutover/);
  assert.match(activate, /primary_domain=ygo\.grand-umi\.com/);
  assert.match(activate, /旧主域未拒绝访问/);
});

test("直连启用前必须完成 DNS 独占、证书主机名和活动槽运行时配置校验", () => {
  assert.match(directTls, /direct\.grand-umi\.com/);
  assert.match(directTls, /resolved_ipv4/);
  assert.match(directTls, /103\.146\.230\.37/);
  assert.match(directTls, /openssl x509[\s\S]*-checkhost/);
  assert.match(directTls, /network-endpoints\.json/);
  assert.match(directTls, /wss:\/\/direct\.grand-umi\.com\/ws/);
  assert.match(directTls, /backend\/ready/);
  assert.match(directTls, /--key-type rsa --rsa-key-size 2048/);
  assert.match(directTls, /grandumi-direct-certificate/);
  assert.match(directTlsRenewHook, /isrg-root-x2-cross-signed\.pem/);
  assert.match(directTlsRenewHook, /tail -c "\$compat_bytes"/);
  assert.match(directTlsRenewHook, /openssl x509[\s\S]*-checkhost/);
  assert.match(directTlsCompatChain, /BEGIN CERTIFICATE/);
  assert.match(directTlsCompatChain, /END CERTIFICATE/);
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

test("应急直连中转按持久主域模式安全选择上游并保留自动回滚", () => {
  assert.match(emergencyDirectRelay, /direct\.grand-umi\.com/);
  assert.match(emergencyDirectRelay, /reverse_proxy https:\/\/103\.146\.230\.37/);
  assert.match(emergencyDirectRelay, /header_up Host __GRANDUMI_PRIMARY_DOMAIN__/);
  assert.match(emergencyDirectRelay, /tls_server_name __GRANDUMI_PRIMARY_DOMAIN__/);
  assert.doesNotMatch(emergencyDirectRelay, /header_up Host (?:grand-umi|ygo\.grand-umi)\.com/);
  assert.match(enableEmergencyDirectRelay, /primary-domain-mode/);
  assert.match(enableEmergencyDirectRelay, /legacy\) upstream_host=grand-umi\.com/);
  assert.match(enableEmergencyDirectRelay, /ygo\) upstream_host=ygo\.grand-umi\.com/);
  assert.match(enableEmergencyDirectRelay, /未知正式主域模式/);
  assert.match(enableEmergencyDirectRelay, /flock -n 9/);
  assert.match(enableEmergencyDirectRelay, /--resolve "\$upstream_host:443:103\.146\.230\.37"/);
  assert.match(enableEmergencyDirectRelay, /"https:\/\/\$upstream_host\/backend\/ready"/);
  assert.match(enableEmergencyDirectRelay, /placeholder_count[\s\S]*-eq 2/);
  assert.match(enableEmergencyDirectRelay, /direct\.grand-umi\.com\.caddy\.pre-relay-/);
  assert.match(enableEmergencyDirectRelay, /rollback\(\)/);
  assert.match(enableEmergencyDirectRelay, /caddy validate/);
  assert.match(enableEmergencyDirectRelay, /systemctl reload caddy/);
});
