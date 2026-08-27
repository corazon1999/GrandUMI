import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const root = new URL("../../", import.meta.url);
const read = (path) => readFile(new URL(path, root), "utf8");

const [
  program,
  sharedDatabase,
  qqStore,
  bridge,
  session,
  backendProject,
  testService,
  productionService,
  testDeploy,
  migration,
  productionSwitch,
  productionSnapshot,
  stage,
  activate,
  adminPanel,
  protocol,
  types,
] = await Promise.all([
  read("服务端WebSocket/Program.cs"),
  read("服务端WebSocket/Persistence/SharedAccountDatabase.cs"),
  read("服务端WebSocket/Persistence/QqAccessStore.cs"),
  read("服务端WebSocket/WebSocketBridge.cs"),
  read("服务端WebSocket/WsSession.cs"),
  read("服务端WebSocket/GrandUMIServer.csproj"),
  read("ops/server/grandumi-test-backend.service"),
  read("ops/server/grandumi-production-backend@.service"),
  read("ops/server/deploy-test.sh"),
  read("ops/server/grandumi-shared-account-migration.sh"),
  read("ops/server/grandumi-production-switch.sh"),
  read("ops/server/grandumi-production-snapshot.sh"),
  read("ops/server/stage-grandumi-production.sh"),
  read("ops/server/activate-grandumi-production.sh"),
  read("opcgpro-web/src/components/home/AdminPanel.tsx"),
  read("opcgpro-web/src/net/HomeProtocol.ts"),
  read("opcgpro-web/src/types/net.ts"),
]);

test("测试服部署只准备共享目录，不迁移正式账号也不激活", () => {
  assert.match(testService, /GRANDUMI_ACCOUNT_DB=\/data\/grandumi-shared\/accounts\.db/);
  assert.match(testService, /GRANDUMI_ACCOUNT_DB_ACTIVATION_MARKER=\/data\/grandumi-shared\/active/);
  assert.match(testService, /GRANDUMI_ACCOUNT_DB_ALLOW_LOCAL_FALLBACK=1/);
  assert.match(testService, /GRANDUMI_ACCOUNT_DB_PREPARED_MARKER=\/data\/grandumi-shared\/prepared/);
  assert.match(testService, /ReadOnlyPaths=\/data\/grandumi/);
  assert.match(testDeploy, /install -d -o grandumi -g grandumi -m 0750 \/data\/grandumi-shared/);
  assert.match(testDeploy, /\.grandumi-shared-account-v1/);
  assert.doesNotMatch(testDeploy, /--migrate-shared-accounts/);
  assert.doesNotMatch(testDeploy, /\/data\/grandumi\/players\.db/);
  assert.doesNotMatch(testDeploy, />\s*"?\/data\/grandumi-shared\/active/);
  assert.match(testDeploy, /grandumi-account-authority-cutover\.lock/);
});

test("首次正式切换在停写后迁移并先提交共享权威，再启动正式服和测试服", () => {
  assert.match(program, /--migrate-shared-accounts/);
  assert.match(program, /new\("production", primaryPath, Authoritative: true\)/);
  assert.match(migration, /formal_backend_is_active[\s\S]*必须停止所有正式后端写入/);
  assert.match(migration, /test_backend_is_active[\s\S]*必须停止测试后端写入/);
  assert.match(migration, /accounts\.db\.next\.\$\$/);
  assert.match(migration, /--migrate-shared-accounts "\$next" "\$formal_players"/);
  assert.match(migration, /\[\[ -s "\$test_players" \]\] && arguments\+=/);
  assert.match(migration, /PRAGMA wal_checkpoint\(TRUNCATE\); PRAGMA journal_mode=DELETE/);
  assert.match(migration, /mv -f -- "\$next" "\$shared_db"/);
  const replaceDatabase = migration.indexOf('mv -f -- "$next" "$shared_db"');
  const prepareMarker = migration.indexOf("write_prepared_marker", replaceDatabase);
  assert.ok(replaceDatabase >= 0 && prepareMarker > replaceDatabase);
  const stop = productionSwitch.indexOf('systemctl stop "grandumi-production-backend@$active.service"');
  const stopTest = productionSwitch.indexOf("systemctl stop grandumi-test-backend.service");
  const prepare = productionSwitch.indexOf('"$shared_migration" prepare', stop);
  const commit = productionSwitch.indexOf('"$shared_migration" commit-authority', prepare);
  const start = productionSwitch.indexOf('systemctl start "grandumi-production-backend@$target.service"', commit);
  const proxy = productionSwitch.indexOf('write_proxy "$backend_port"', start);
  const activateTest = productionSwitch.indexOf('"$shared_migration" activate-test', proxy);
  assert.ok(
    stopTest >= 0 && stop >= 0 && prepare > stopTest && prepare > stop
      && commit > prepare && start > commit && proxy > start && activateTest > proxy,
  );
  assert.match(
    productionSwitch,
    /test_backend_was_active[\s\S]*systemctl start grandumi-test-backend\.service \|\| true/,
  );
  const legacyStopTest = activate.indexOf("systemctl stop grandumi-test-backend.service");
  const legacyPrepare = activate.indexOf('"$shared_migration" prepare');
  const legacyCommit = activate.indexOf('"$shared_migration" commit-authority', legacyPrepare);
  const legacyStart = activate.indexOf("systemctl start grandumi-production-backend@a.service", legacyCommit);
  assert.ok(
    legacyStopTest >= 0 && legacyPrepare > legacyStopTest
      && legacyCommit > legacyPrepare && legacyStart > legacyCommit,
  );
  assert.match(
    activate,
    /test_backend_was_active[\s\S]*systemctl start grandumi-test-backend\.service \|\| true/,
  );
  assert.match(migration, /激活标记不得再撤销/);
  assert.doesNotMatch(migration, /rm -f -- "\$active_marker"/);
  assert.match(migration, /commit-authority/);
  assert.match(migration, /systemctl restart grandumi-test-backend\.service/);
  assert.match(productionSwitch, /shared_authority_committed[\s\S]*禁止回滚旧本地账号库/);
  assert.match(
    productionSwitch,
    /shared_active_marker[\s\S]*active\/backend\/\.grandumi-shared-account-v1[\s\S]*shared_authority_committed=1/,
  );
  assert.match(activate, /shared_authority_committed[\s\S]*禁止恢复旧账号库/);
  assert.match(productionSwitch, /grandumi-account-authority-cutover\.lock/);
  assert.match(activate, /grandumi-account-authority-cutover\.lock/);
});

test("共享库激活后快照会包含账号库且旧槽位不得回滚", () => {
  assert.match(backendProject, /shared-account-v1\.marker/);
  assert.match(backendProject, /TargetPath="\.grandumi-shared-account-v1"/);
  assert.match(stage, /publish_next\/\.grandumi-shared-account-v1/);
  assert.match(productionService, /GRANDUMI_ACCOUNT_DB=\/data\/grandumi-shared\/accounts\.db/);
  assert.match(productionService, /GRANDUMI_ACCOUNT_DB_ACTIVATION_MARKER=\/data\/grandumi-shared\/active/);
  assert.match(productionService, /GRANDUMI_ACCOUNT_DB_PREPARED_MARKER=\/data\/grandumi-shared\/prepared/);
  assert.match(sharedDatabase, /共享账号库不存在或为空，拒绝在服务启动时自动创建/);
  assert.match(sharedDatabase, /HasCompletedLegacyMigration/);
  assert.ok(
    program.indexOf("SharedAccountDatabase.ResolveDefaultPath")
      < program.indexOf("playerDataStore.Initialize()"),
    "共享账号启动门禁必须早于玩法库初始化写入",
  );
  assert.match(program, /usesIndependentSharedAccountDatabase\s*\? null/);
  assert.match(program, /requirePreparedMigration: usesIndependentSharedAccountDatabase/);
  assert.match(migration, /shared_account_migration_audit WHERE schema_version=1 AND source_count>0/);
  assert.match(productionService, /ReadWritePaths=\/data\/grandumi \/data\/grandumi-shared/);
  assert.match(productionSwitch, /shared_active_marker=\/data\/grandumi-shared\/active/);
  assert.match(productionSwitch, /target_backend\/\.grandumi-shared-account-v1/);
  assert.match(productionSnapshot, /shared_database=\/data\/grandumi-shared\/accounts\.db/);
  assert.match(productionSnapshot, /database_sources\+=\("\$shared_database"\)/);
  assert.match(activate, /"\$shared_migration" prepare/);
  assert.match(activate, /"\$shared_migration" activate-test/);
});

test("管理员可按账号昵称或 QQ 反查，改绑使用修订号和幂等请求", () => {
  assert.match(qqStore, /SearchAccountsForAdmin/);
  assert.match(qqStore, /matchKind/);
  assert.match(qqStore, /shared_qq_binding_requests/);
  assert.match(qqStore, /expectedRevision/);
  assert.match(qqStore, /Guid\.TryParse\(requestId/);
  assert.match(qqStore, /shared_account_security_events/);
  assert.match(bridge, /AdministratorPolicy\.IsAuthorized\(session\.Account\)/);
  assert.match(bridge, /action == "setQq" \? "set" : "unbind"/);
  assert.match(adminPanel, /playerSearchBy/);
  assert.match(adminPanel, /QQ 号反查/);
  assert.match(adminPanel, /crypto\.randomUUID\(\)/);
  assert.match(adminPanel, /selectedPlayer\.bindingRevision/);
  assert.match(protocol, /searchAdminPlayers\(query: string, searchBy/);
  assert.match(protocol, /setAdminPlayerQq/);
  assert.match(protocol, /unbindAdminPlayerQq/);
  assert.match(types, /bindingRevision\?: number/);
  assert.match(types, /matchKind\?: "account_exact" \| "nickname_exact" \| "qq_exact" \| "fuzzy"/);
  assert.match(adminPanel, /min-h-11/);
});

test("管理员改绑强制重新认证，已注册对局仅续到本局结束", () => {
  assert.match(bridge, /ApplyQqBindingSecurityMutation/);
  assert.match(bridge, /IsRegisteredPlayerSession\(session\)/);
  assert.match(bridge, /currentGameContinues = true/);
  assert.match(bridge, /SupersedeSession\(session, "QQ 绑定已被管理员更新/);
  assert.match(bridge, /session\.IsQqAccessRevoked && !IsRegisteredPlayerSession\(session\)/);
  assert.match(session, /TryStartQqRevokedCloseMonitor/);
  assert.match(bridge, /restrictedGameRecovery = GameRoomManager\.HasActivePlayerAccount/);
});

test("共享数据库对 QQ 唯一性、审计和安全事件建立持久化约束", () => {
  assert.match(sharedDatabase, /CREATE UNIQUE INDEX IF NOT EXISTS ux_shared_account_qq_bindings_qq/);
  assert.match(sharedDatabase, /shared_admin_player_audit/);
  assert.match(sharedDatabase, /shared_account_security_events/);
  assert.match(sharedDatabase, /PRAGMA integrity_check/);
});
