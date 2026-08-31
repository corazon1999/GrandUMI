import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const root = new URL("../../", import.meta.url);
const read = (path) => readFile(new URL(path, root), "utf8");

const [
  store,
  roomManager,
  program,
  bridge,
  panel,
  history,
  feedback,
  replayPage,
  protocol,
  types,
  contract,
] = await Promise.all([
  read("服务端WebSocket/Persistence/CloudReplayStore.cs"),
  read("服务端WebSocket/Game/GameRoomManager.cs"),
  read("服务端WebSocket/Program.cs"),
  read("服务端WebSocket/WebSocketBridge.cs"),
  read("opcgpro-web/src/components/home/CloudReplayPanel.tsx"),
  read("opcgpro-web/src/components/home/HistoryPanel.tsx"),
  read("opcgpro-web/src/components/game/FeedbackOverlay.tsx"),
  read("opcgpro-web/src/app/replay/[id]/page.tsx"),
  read("opcgpro-web/src/net/HomeProtocol.ts"),
  read("opcgpro-web/src/types/net.ts"),
  read("protocol/contracts/websocket.v1.json").then(JSON.parse),
]);

const cloudProtocols = [
  "MsgCloudReplayList",
  "MsgCloudReplayLoad",
  "MsgCloudReplayBookmark",
  "MsgCloudReplayShare",
  "MsgCloudReplayDelete",
];

test("云回放只为新完成对局分别记录参与者权威视角，并在完整终局后发布", () => {
  assert.match(roomManager, /CloudReplay\?\.AppendSnapshot\(idx, payload\)/);
  assert.match(roomManager, /new CloudReplayPlayer\(p0Account[\s\S]*Record: true/);
  assert.match(roomManager, /new CloudReplayPlayer\(p1Account[\s\S]*Record: !vsBot/);
  assert.match(roomManager, /State\.IsGameOver[\s\S]*CloudReplay\.CompleteAsync[\s\S]*CloudReplay\.AbortAsync/);
  assert.match(store, /viewerKind[\s\S]*"player"/);
  assert.match(store, /if \(!TryBoolean\(snapshots\[\^1\], "isGameOver"\)\)/);
  assert.match(store, /opponent\.GetProperty\("handCardIds"\)\.GetArrayLength\(\) != 0/);
  assert.match(store, /_active\.TryRemove[\s\S]*CloseCaptureFiles[\s\S]*WriteDocumentAtomic/);
  assert.match(store, /_writer\.Close\(key\)[\s\S]*TryDeleteFile\(path\)/);
});

test("账号授权、分享脱敏、幂等和历史运行时边界均由服务端强制执行", () => {
  assert.match(bridge, /!session\.IsLoggedIn \|\| !IsCurrentAccountSession\(session\)/);
  assert.match(bridge, /cloud-replay-list/);
  assert.match(bridge, /cloud-replay-load/);
  assert.match(bridge, /cloud-replay-mutation/);
  assert.match(store, /WHERE replay_id = \$replayId AND owner_account = \$owner/);
  assert.match(store, /share_token_hash = \$tokenHash/);
  assert.match(store, /SHA256\.HashData/);
  assert.match(store, /TryReadMutation<CloudReplayShareResult>/);
  assert.match(store, /request_conflict/);
  assert.match(store, /CloudReplaySharePolicies\.Masked/);
  assert.match(store, /ScrubPlayerHand\(snapshot\["my"\]/);
  assert.match(store, /ScrubPlayerHand\(snapshot\["opponent"\]/);
  assert.match(store, /snapshot\["pendingPrompt"\] = null/);
  assert.match(store, /"runtime_missing"/);
  assert.match(program, /GRANDUMI_CLOUD_REPLAY_RUNTIME_ARCHIVE_ROOT/);
});

test("云回放具备查询、书签、反馈关联、留存、硬配额和永久删除生命周期", () => {
  assert.match(store, /query\.Opponent/);
  assert.match(store, /query\.Outcome/);
  assert.match(store, /query\.MatchKind/);
  assert.match(store, /query\.BookmarkedOnly/);
  assert.match(store, /public bool AssociateFeedback/);
  assert.match(store, /DefaultRetentionDays = 90/);
  assert.match(store, /DefaultMaximumReplays = 100/);
  assert.match(store, /DefaultQuotaBytes = 256L \* 1024 \* 1024/);
  assert.match(store, /if \(row\.Bookmarked\) continue/);
  assert.match(store, /if \(used <= _quotaBytes\) break/);
  assert.match(store, /DELETE FROM cloud_replays WHERE replay_id = \$replayId AND owner_account = \$owner/);
  assert.match(store, /TryNormalizeReplayId/);
  assert.match(bridge, /TryNormalizeReplayId\(requestedReplayValue/);
  assert.match(bridge, /AssociateFeedback\(submitterAccount, replayId, feedbackId\)/);
});

test("前端提供完整云回放入口并拒绝迟到或乱序响应", () => {
  assert.match(history, /CloudReplayPanel/);
  assert.match(panel, /opponent/);
  assert.match(panel, /bookmarkedOnly/);
  assert.match(panel, /full_timeline/);
  assert.match(panel, /runtime_missing/);
  assert.match(panel, /latestListRequest/);
  assert.match(panel, /latestLoadRequest/);
  assert.match(panel, /latestMutationRequest/);
  assert.match(panel, /pending\.requestId !== message\.requestId/);
  assert.match(panel, /window\.setTimeout[\s\S]*10_000/);
  assert.match(panel, /min-h-11/);
  assert.match(panel, /grid-cols-1/);
  assert.match(feedback, /replayId/);
  assert.match(replayPage, /readCloudReplayLink/);
});

test("协议契约和双端类型覆盖全部云回放请求，关键写操作都携带 requestId", () => {
  for (const protoName of cloudProtocols) {
    assert.ok(contract.clientToServer.includes(protoName), `${protoName} 缺少客户端到服务端契约`);
    assert.ok(contract.serverToClient.includes(protoName), `${protoName} 缺少服务端到客户端契约`);
    assert.match(protocol, new RegExp(protoName));
    assert.match(types, new RegExp(`interface ${protoName}`));
    assert.ok(contract.criticalMessages.clientToServer[protoName].includes("requestId"));
    assert.ok(contract.criticalMessages.serverToClient[protoName].includes("requestId"));
  }
});
