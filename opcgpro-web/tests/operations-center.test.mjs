import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const read = (relative) => readFile(new URL(relative, import.meta.url), "utf8");

const [bridge, program, playerStore, operationsStore, protocol, netTypes, contract, workbench, adminPanel, layoutPreview] = await Promise.all([
  read("../../服务端WebSocket/WebSocketBridge.cs"),
  read("../../服务端WebSocket/Program.cs"),
  read("../../服务端WebSocket/Persistence/PlayerDataStore.cs"),
  read("../../服务端WebSocket/Persistence/OperationsCenterStore.cs"),
  read("../src/net/HomeProtocol.ts"),
  read("../src/types/net.ts"),
  read("../../protocol/contracts/websocket.v1.json"),
  read("../src/components/home/OperationsWorkbench.tsx"),
  read("../src/components/home/AdminPanel.tsx"),
  read("../src/components/home/OperationsWorkbenchLayoutVerification.tsx"),
]);

test("运营中心在启动时接入统一 Case 与一致性 Doctor", () => {
  assert.match(program, /new OperationsCenterStore/);
  assert.match(program, /new ConsistencyDoctor/);
  assert.match(program, /consistencyDoctor\.RunOnce\(\)/);
  assert.match(program, /RunLoopAsync/);
  assert.match(program, /operationsCenterStore\.Dispose\(\)/);
});

test("管理协议、玩家申诉与报告入口均路由到权威存储", () => {
  for (const proto of [
    "MsgOperationsCases", "MsgOperationsCaseDetail", "MsgOperationsCaseUpdate",
    "MsgOperationsCaseAppeal", "MsgOperationsPenalty", "MsgPrivilegedAudit",
    "MsgConsistencyDoctor", "MsgAdminApproval",
  ]) {
    assert.match(bridge, new RegExp(`case "${proto}"`));
    assert.match(netTypes, new RegExp(`proto: "${proto}"`));
    assert.match(protocol, new RegExp(`case "${proto}"`));
  }
  assert.match(bridge, /OperationsCaseSources\.PlayerReport/);
  assert.match(bridge, /OperationsCaseSources\.BugReport/);
  assert.match(bridge, /new OperationsCaseEvidenceInput\(\s*"game_chat"/);
});

test("处罚在匹配、聊天和观战入口执行且不存在永久处罚路径", () => {
  assert.match(bridge, /restrictions\.MatchBanned/);
  assert.match(bridge, /restrictions\.Muted \|\| restrictions\.SpectateOrChatBanned/);
  assert.match(bridge, /forSpectating: true/);
  assert.match(operationsStore, /MaximumPenaltyDays = 365/);
  assert.match(operationsStore, /expires <= now\.UtcDateTime\.AddMinutes\(1\)/);
});

test("发布、密码重置和数据库修复均消费 Web 管理端一次性确认", () => {
  assert.match(bridge, /"deploy_production" : "deploy_test"/);
  assert.match(bridge, /"reset_password", targetAccount/);
  assert.match(bridge, /"database_repair", findingId\.ToString\(\)/);
  assert.match(operationsStore, /source, "web_admin"/);
  assert.match(operationsStore, /consumed_at_ms IS NULL/);
});

test("玩家昵称只写事务 outbox，WebSocket 不再直接跨库双写", () => {
  assert.match(playerStore, /display_name_sync_outbox/);
  assert.match(playerStore, /EnqueueDisplayNameSync/);
  assert.doesNotMatch(bridge, /UpdateDirectorySearchName\(/);
});

test("协议契约同时登记全部运营中心双向消息", () => {
  const parsed = JSON.parse(contract);
  for (const proto of [
    "MsgOperationsCases", "MsgOperationsCaseDetail", "MsgOperationsCaseUpdate",
    "MsgOperationsCaseAppeal", "MsgOperationsPenalty", "MsgPrivilegedAudit",
    "MsgConsistencyDoctor", "MsgAdminApproval",
  ]) {
    assert.ok(parsed.clientToServer.includes(proto), `${proto} 缺少入站契约`);
    assert.ok(parsed.serverToClient.includes(proto), `${proto} 缺少出站契约`);
  }
});

test("运维工作台覆盖 Case、P90、处罚、审计和一致性修复", () => {
  for (const marker of [
    "data-operations-workbench", "data-operations-case-list", "data-operations-case-detail",
    "data-operations-audit", "data-operations-doctor",
  ]) assert.match(workbench, new RegExp(marker));
  assert.match(workbench, /firstActionP90Ms/);
  assert.match(workbench, /applyOperationsPenalty/);
  assert.match(workbench, /revokeOperationsPenalty/);
  assert.match(workbench, /requestPrivilegedAudit/);
  assert.match(workbench, /requestAdminApproval\("database_repair"/);
  assert.match(workbench, /repairConsistencyFinding/);
});

test("发布与密码重置在网页端实行申请凭证和二次执行", () => {
  assert.match(adminPanel, /requestAdminApproval\(operation, environment\)/);
  assert.match(adminPanel, /deployLatest\(environment, adminApproval\)/);
  assert.doesNotMatch(adminPanel, /deployLatest\(environment\)\)/);
  assert.match(adminPanel, /requestAdminApproval\("reset_password", selectedPlayer\.account\)/);
  assert.match(adminPanel, /resetAdminPlayerPassword\(selectedPlayer\.account, adminApproval\)/);
  assert.doesNotMatch(adminPanel, /resetAdminPlayerPassword\(selectedPlayer\.account\);/);
});

test("运维工作台为手机竖屏提供单列断点、安全区和 44px 触控目标", () => {
  assert.match(workbench, /var\(--layout-safe-bottom,env\(safe-area-inset-bottom\)\)/);
  assert.match(workbench, /@\[620px\]:grid-cols-2/);
  assert.match(workbench, /@\[820px\]:grid-cols-\[minmax\(16rem,0\.75fr\)_minmax\(0,1\.25fr\)\]/);
  assert.match(workbench, /min-h-11/);
  assert.match(layoutPreview, /data-operations-workbench-layout-verification/);
  assert.match(layoutPreview, /overflow-y-auto overflow-x-hidden/);
});

test("全部既有管理员写操作先写审计意图并记录最终结果", () => {
  assert.match(bridge, /_operationsCenterStore\s*\?\? throw new OperationsCenterException\("audit_unavailable"/);
  for (const operation of [
    "card_back_delete", "card_back_review", "global_announcement",
    "maintenance_set", "ruleset_activate", "qq_whitelist_import",
    "admin_player_search",
  ]) {
    assert.match(bridge, new RegExp(`RequirePrivilegedAuditIntent\\([\\s\\S]{0,180}"${operation}`));
    assert.match(bridge, new RegExp(`CompletePrivilegedAudit\\([\\s\\S]{0,180}"${operation}`));
  }
  assert.match(bridge, /auditOperation = \$"admin_player_/);
  assert.match(bridge, /RequirePrivilegedAuditIntent\([\s\S]{0,160}auditOperation/);
  assert.match(bridge, /CompletePrivilegedAudit\([\s\S]{0,160}auditOperation/);
  assert.match(operationsStore, /AppendAuditCore\([\s\S]{0,220}"case_transition"/);
  assert.match(operationsStore, /AppendAuditCore\([\s\S]{0,220}"penalty_apply"/);
  assert.match(operationsStore, /AppendAuditCore\([\s\S]{0,220}"penalty_revoke"/);
  assert.match(operationsStore, /AppendAuditCore\([\s\S]{0,220}"high_risk_challenge"/);
  assert.match(operationsStore, /AppendAuditCore\([\s\S]{0,220}"consistency_repair_queue"/);
});
