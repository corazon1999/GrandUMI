import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const root = new URL("../../", import.meta.url);
const read = (path) => readFile(new URL(path, root), "utf8");

const [
  backendTemplate,
  frontendTemplate,
  healthCheck,
  healthTimer,
  switchScript,
  stageScript,
  proxy,
  productionSlice,
  buildSlice,
  activateScript,
  backendProject,
  productionSnapshot,
] = await Promise.all([
  read("ops/server/grandumi-production-backend@.service"),
  read("ops/server/grandumi-production-frontend@.service"),
  read("ops/server/grandumi-production-health-check.sh"),
  read("ops/server/grandumi-production-health.timer"),
  read("ops/server/grandumi-production-switch.sh"),
  read("ops/server/stage-grandumi-production.sh"),
  read("ops/server/grandumi-production-proxy.nginx"),
  read("ops/server/grandumi-production.slice"),
  read("ops/server/grandumi-build.slice"),
  read("ops/server/activate-grandumi-production.sh"),
  read("服务端WebSocket/GrandUMIServer.csproj"),
  read("ops/server/grandumi-production-snapshot.sh"),
]);

test("A/B 后端共享正式数据但由应用单写租约防双写", () => {
  assert.match(backendTemplate, /GRANDUMI_DATA_DIR=\/data\/grandumi/);
  assert.match(backendTemplate, /GRANDUMI_REQUIRE_SINGLE_WRITER=1/);
  assert.match(backendTemplate, /backend-%i\.env/);
  assert.match(backendTemplate, /Restart=always/);
  assert.match(backendTemplate, /StartLimitBurst=8/);
  assert.match(backendTemplate, /Slice=grandumi-production\.slice/);
  assert.match(frontendTemplate, /frontend-%i\.env/);
  assert.match(frontendTemplate, /slots\/%i\/frontend\/node_modules\/next/);
  assert.match(frontendTemplate, /^SuccessExitStatus=143$/m);
});

test("健康检查连续三次失败才自愈，并优先原槽重启", () => {
  assert.match(healthTimer, /OnUnitActiveSec=5s/);
  assert.match(healthCheck, /failures >= 3/);
  assert.match(healthCheck, /:\$port\/live/);
  assert.doesNotMatch(healthCheck, /:\$port\/ready/);
  const restart = healthCheck.indexOf("systemctl restart");
  const failover = healthCheck.indexOf("--failover");
  assert.ok(restart >= 0 && failover > restart);
});

test("蓝绿切换先验证目标，失败自动恢复原槽", () => {
  assert.match(switchScript, /trap rollback ERR/);
  assert.match(switchScript, /grandumi-production-backend@\$target\.service/);
  assert.match(switchScript, /retry 25/);
  assert.match(switchScript, /active_file\.next/);
  assert.match(switchScript, /previous_target_backend/);
  assert.match(switchScript, /systemctl enable "grandumi-production-backend@\$target\.service"/);
  assert.match(proxy, /grandumi-active-backend\.conf/);
  assert.match(proxy, /grandumi-active-frontend\.conf/);
  assert.match(proxy, /grandumi-active-assets\.conf/);
  assert.match(switchScript, /grandumi-active-assets\.conf\.next/);
  assert.match(activateScript, /systemctl disable grandumi-production-backend\.service/);
  assert.match(activateScript, /grandumi-production-switch --release/);
  assert.match(activateScript, /GrandUMIServer\.dll" \]\] \\\n\s+&& systemctl is-active/);
  assert.match(activateScript, /systemctl is-active --quiet "grandumi-production-backend@\$active_slot\.service"/);
  assert.match(activateScript, /systemctl is-active --quiet "grandumi-production-frontend@\$active_slot\.service"/);
  assert.match(activateScript, /data_source=existing/);
  assert.match(activateScript, /data_source=import/);
  assert.match(activateScript, /拒绝覆盖或激活/);
  assert.match(activateScript, /if \[\[ "\$data_source" == import \]\]/);
});

test("QQ 白名单生效后只允许回退到具备同等准入能力的槽位", () => {
  assert.match(backendProject, /qq-access-enforcement-v1\.marker/);
  assert.match(backendProject, /TargetPath="\.grandumi-qq-access-enforcement-v1"/);
  assert.match(stageScript, /publish_next\/\.grandumi-qq-access-enforcement-v1/);
  assert.match(stageScript, /缺少 QQ 准入兼容标记/);
  assert.match(switchScript, /sqlite_master[\s\S]*qq_whitelist_state/);
  assert.match(switchScript, /SELECT count\(\*\) FROM qq_whitelist_state WHERE singleton_id=1/);
  assert.match(switchScript, /marker="\$target_backend\/\.grandumi-qq-access-enforcement-v1"/);
  assert.match(switchScript, /拒绝回退到旧版本/);
  assert.match(
    switchScript,
    /--release\)[\s\S]*verify_qq_access_rollback_compatibility "\$release_root\/\$release\/backend"[\s\S]*ln -sfn/,
  );
  assert.match(
    switchScript,
    /--failover\)[\s\S]*verify_qq_access_rollback_compatibility "\$slot_root\/\$target\/backend"/,
  );
  assert.match(activateScript, /converge_standby_release/);
  assert.match(activateScript, /systemctl is-active --quiet "grandumi-production-backend@\$standby\.service"/);
  assert.match(activateScript, /ln -sfn "\$expected_backend" "\$repo\/slots\/\$standby\/backend"/);
  assert.match(activateScript, /standby-release\.next/);
  const releaseSwitch = activateScript.indexOf('grandumi-production-switch --release "$target"');
  const routeVerification = activateScript.indexOf("verify_production_routes", releaseSwitch);
  const standbyConvergence = activateScript.indexOf("converge_standby_release", releaseSwitch);
  assert.ok(releaseSwitch >= 0 && routeVerification > releaseSwitch && standbyConvergence > routeVerification);
});

test("正式切槽前对全部 SQLite 做在线一致性快照并校验离线副本", () => {
  assert.match(productionSnapshot, /data_dir=\/data\/grandumi/);
  assert.match(productionSnapshot, /archive_root=\/data\/grandumi-archives/);
  assert.match(productionSnapshot, /required_databases=\(players\.db ranked\.db leader-stats\.db\)/);
  assert.match(productionSnapshot, /sqlite3 -readonly "\$database"[\s\S]*\.backup '\$destination'/);
  assert.match(productionSnapshot, /sqlite3 "\$destination" 'PRAGMA integrity_check;'/);
  assert.match(productionSnapshot, /sha256sum "\$destination"/);
  assert.match(productionSnapshot, /find "\$data_dir" -maxdepth 1 -type f -name '\*\.db'/);
  assert.match(productionSnapshot, /"\$target\/\.complete"/);
  const snapshot = activateScript.indexOf('grandumi-production-snapshot "$target"');
  const switchSlot = activateScript.indexOf('grandumi-production-switch --release "$target"');
  assert.ok(snapshot >= 0 && switchSlot > snapshot);
  assert.match(activateScript, /-f "\$snapshot_archive\/\.complete"/);
  assert.match(stageScript, /grandumi-production-snapshot\.sh" \/usr\/local\/sbin\/grandumi-production-snapshot/);
});

test("发布构建进入低优先级资源组且产物按提交隔离", () => {
  assert.match(stageScript, /--slice=grandumi-build\.slice/);
  assert.match(stageScript, /\/usr\/bin\/bash "\$stage_script" "\$target"/);
  assert.match(stageScript, /worktree add --detach/);
  assert.doesNotMatch(stageScript, /checkout --detach/);
  assert.doesNotMatch(stageScript, /\.next\.production-previous/);
  assert.match(stageScript, /rsync -a --delete --link-dest/);
  assert.match(stageScript, /rsync -a --ignore-existing "\$previous_frontend\/\.next\/static\/"/);
  assert.match(stageScript, /node_modules\/ "\$frontend_next\/node_modules\/"/);
  assert.match(stageScript, /previous_frontend\/node_modules/);
  assert.match(stageScript, /shared_asset_root=\/www/);
  assert.match(stageScript, /ln -s "\$shared_asset_root\/\$asset_dir" "\$slot_asset_path"/);
  assert.match(stageScript, /releases\/\$target/);
  assert.match(productionSlice, /MemoryMax=6500M/);
  assert.match(buildSlice, /CPUQuota=200%/);
  assert.match(buildSlice, /MemoryMax=3G/);
  assert.match(buildSlice, /IOWeight=10/);
});
