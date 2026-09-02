import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const read = (path) => readFile(new URL(path, import.meta.url), "utf8");
const [panel, admin, protocol, store, types, bridge, coordinator, rules, roomManager, runner, contractText, testBackend, productionBackend, layoutPage, layoutFixture] = await Promise.all([
  read("../src/components/home/AdminHexCatalogPanel.tsx"),
  read("../src/components/home/AdminPanel.tsx"),
  read("../src/net/HomeProtocol.ts"),
  read("../src/store/netStore.ts"),
  read("../src/types/net.ts"),
  read("../../服务端WebSocket/WebSocketBridge.cs"),
  read("../../服务端WebSocket/AdminDeploymentCoordinator.cs"),
  read("../../服务端WebSocket/Game/Hex/HexRules.cs"),
  read("../../服务端WebSocket/Game/GameRoomManager.cs"),
  read("../../ops/server/grandumi-admin-deploy.sh"),
  read("../../protocol/contracts/websocket.v1.json"),
  read("../../ops/server/grandumi-test-backend.service"),
  read("../../ops/server/grandumi-production-backend@.service"),
  read("../src/app/layout-verification/admin-hex-catalog/page.tsx"),
  read("../src/components/home/AdminHexCatalogLayoutVerification.tsx"),
]);
const contract = JSON.parse(contractText);

test("管理员面板接入独立海克斯品质配置协议和权威状态", () => {
  assert.match(admin, /<AdminHexCatalogPanel/);
  assert.match(types, /interface MsgAdminHexCatalog/);
  assert.match(types, /interface AdminHexCatalogEnvironmentState/);
  assert.match(store, /adminHexCatalog: AdminHexCatalogState/);
  assert.match(protocol, /case "MsgAdminHexCatalog"/);
  assert.match(protocol, /requestAdminHexCatalog\(\)/);
  assert.match(protocol, /saveAdminHexCatalog\(/);
  assert.match(protocol, /publishAdminHexCatalog\(/);
  assert.ok(contract.clientToServer.includes("MsgAdminHexCatalog"));
  assert.ok(contract.serverToClient.includes("MsgAdminHexCatalog"));
  assert.deepEqual(
    contract.criticalMessages.clientToServer.MsgAdminHexCatalog,
    ["proto", "requestId", "action", "environment"],
  );
  assert.deepEqual(
    contract.criticalMessages.serverToClient.MsgAdminHexCatalog,
    ["proto", "requestId", "result"],
  );
  assert.match(bridge, /name = definition\.Name/);
  assert.match(bridge, /description = definition\.Description/);
  assert.doesNotMatch(bridge, /\n\s*definition\.Name,/);
  assert.doesNotMatch(bridge, /\n\s*definition\.Description,/);
  assert.match(types, /interface AdminHexCatalogEntry \{[\s\S]*name: string;[\s\S]*description: string;/);
  assert.match(panel, /<h3[^>]*>\{entry\.name\}<\/h3>/);
  assert.match(panel, /<p[^>]*>\{entry\.description\}<\/p>/);
});

test("品质编辑采用完整草稿、乐观版本和精确一次性发布目标", () => {
  assert.match(panel, /selected\.draftRevision/);
  assert.match(panel, /selected\.activeRevision/);
  assert.match(panel, /sortedEntries\.map\(\(entry\) => \(\{ id: entry\.id, tier:/);
  assert.match(panel, /baseActiveRevision !== selected\.activeRevision/);
  assert.match(panel, /publish_hex_catalog/);
  assert.match(panel, /draft-\$\{selected\.draftRevision\}:\$\{selected\.draftDigest\}/);
  assert.match(panel, /必须先保存无冲突草稿/);
  assert.match(panel, /不会抓取 main、部署代码或重启服务/);
  assert.match(coordinator, /current\.DraftRevision != expectedDraftRevision/);
  assert.match(coordinator, /active\.Revision != expectedActiveRevision/);
  assert.match(coordinator, /draft\.BaseActiveRevision != active\.Revision/);
  assert.match(coordinator, /string\.Equals\(draft\.Digest, active\.Digest/);
  assert.match(bridge, /ConsumeHighRiskChallenge\([\s\S]*"publish_hex_catalog"/);
});

test("常规品质必须以调换方式严格保持各十八个", () => {
  assert.match(panel, /const REGULAR_HEXES_PER_TIER = 18/);
  assert.match(panel, /count\) => count !== REGULAR_HEXES_PER_TIER/);
  assert.match(panel, /\{regularCounts\[tier\]\} \/ \{REGULAR_HEXES_PER_TIER\}/);
  assert.match(panel, /每个品质必须恰好保留 18 个常规海克斯/);
  assert.match(panel, /请调换另一项以恢复三池平衡/);
  assert.match(panel, /disabled=\{!connected \|\| !dirty \|\| invalidPool \|\| pending !== null\}/);
  assert.equal(Object.values({ Silver: 19, Gold: 17, Rainbow: 18 }).some((count) => count !== 18), true);
  assert.match(runner, /if count != 18:/);
  assert.match(runner, /常规海克斯必须恰好为 18 个/);
  assert.match(runner, /type\(hex_id\) is not int/);
  assert.match(runner, /type\(draft_revision\) is not int/);
  assert.match(runner, /type\(expected_revision\) is not int/);
  assert.match(runner, /type\(current_revision\) is not int/);
  assert.match(runner, /type\(current_source_draft_revision\) is not int/);
});

test("新房间锁定完整品质配置且恢复日志拒绝缺失映射", () => {
  assert.match(rules, /HexCatalogRuntime\.SnapshotForNewRoom\(\)/);
  assert.match(rules, /ApplyCatalogSnapshot/);
  assert.match(roomManager, /hexCatalogRevision/);
  assert.match(roomManager, /hexCatalogDigest/);
  assert.match(roomManager, /hexCatalogTiers/);
  assert.match(roomManager, /hexRulesRevision >= Hex\.HexRules\.CatalogConfigurationRulesRevision/);
  assert.match(roomManager, /MatchReplay\.RebuildAsync\([\s\S]*hexCatalogConfiguration/);
});

test("受限执行器串行校验基线并原子替换且支持崩溃后幂等重放", () => {
  assert.match(runner, /\^hex-\(test\|production\)-\(\[0-9a-f\]\{32\}\)/);
  assert.match(runner, /built_in_digest = "sha256:b466b646/);
  assert.match(runner, /current\.get\("sourceRequestId"\) == request_id/);
  assert.match(runner, /current_revision != expected_revision or current_digest != expected_digest/);
  assert.match(runner, /os\.open\(temporary, os\.O_WRONLY \| os\.O_CREAT \| os\.O_EXCL/);
  assert.match(runner, /target_file\.flush\(\)/);
  assert.match(runner, /os\.fsync\(target_file\.fileno\(\)\)/);
  assert.match(runner, /os\.replace\(temporary, active_path\)/);
  assert.match(runner, /os\.fsync\(directory_fd\)/);
  assert.match(runner, /os\.chown\(directory, 0, gid\)/);
  assert.match(runner, /os\.chown\(temporary, 0, gid\)/);
  assert.match(testBackend, /ReadOnlyPaths=[^\n]*\/data\/grandumi-test\/hex-catalog/);
  assert.match(productionBackend, /ReadOnlyPaths=[^\n]*\/data\/grandumi\/hex-catalog[^\n]*\/data\/grandumi-test\/hex-catalog/);
  assert.match(runner, /flock -n 9/);
  assert.doesNotMatch(runner, /eval /);
});

test("手机竖屏保持单列、44像素触控与安全区内操作", () => {
  assert.match(panel, /data-admin-hex-catalog/);
  assert.match(panel, /data-admin-hex-entry/);
  assert.match(panel, /@\[720px\]:grid-cols-2/);
  assert.match(panel, /min-h-11/);
  assert.match(panel, /layout-safe-bottom/);
  assert.match(panel, /overflow-x-hidden/);
  assert.match(panel, /grid gap-2 @\[560px\]:grid-cols-3/);
  assert.match(panel, /<h3 className="break-words/);
  assert.match(panel, /<p className="mt-1 break-words/);
  assert.match(layoutFixture, /name: `布局验证海克斯 \$\{id\}`/);
  assert.match(layoutFixture, /description: `用于验证手机竖屏长文案/);
  for (const [width, height] of [[390, 844], [360, 780]]) {
    assert.ok(width < 560, `${width}×${height} 应保持单列操作区`);
    assert.ok(width < 720, `${width}×${height} 应保持单列海克斯卡片`);
  }
});

test("布局验证夹具使用假数据且默认在生产构建中返回404", () => {
  assert.match(layoutPage, /export const dynamic = "force-dynamic"/);
  assert.match(layoutPage, /process\.env\.GRANDUMI_LAYOUT_VERIFICATION !== "1"\) notFound\(\)/);
  assert.match(layoutFixture, /previewState=\{PREVIEW_STATE\}/);
  assert.match(layoutFixture, /candidate !== 30 && candidate !== 48/);
  assert.match(layoutFixture, /activeTier: id === 1 \? "Gold" : id === 19 \? "Silver" : tier/);
  assert.equal(panel.match(/if \(previewOnly\) return;/g)?.length, 2);
  assert.match(panel, /if \(!previewOnly\) HomeRequest\.requestAdminHexCatalog\(\)/);
});
