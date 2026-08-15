import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const [store, models, bridge, protocol, panel, main] = await Promise.all([
  readFile(new URL("../../服务端WebSocket/Persistence/PlayerDataStore.cs", import.meta.url), "utf8"),
  readFile(new URL("../../服务端WebSocket/Persistence/PlayerDataModels.cs", import.meta.url), "utf8"),
  readFile(new URL("../../服务端WebSocket/WebSocketBridge.cs", import.meta.url), "utf8"),
  readFile(new URL("../src/net/HomeProtocol.ts", import.meta.url), "utf8"),
  readFile(new URL("../src/components/home/CardBackReviewPanel.tsx", import.meta.url), "utf8"),
  readFile(new URL("../src/components/home/MainPanel.tsx", import.meta.url), "utf8"),
]);

test("新投稿默认待审核且公开广场只读取最多三百款已通过投稿", () => {
  assert.match(store, /MaxCardBackGalleryItems = 300/);
  assert.match(store, /CardBackReviewPending = "pending"/);
  assert.match(store, /VALUES\([\s\S]*\$status, '', NULL, NULL\)/);
  assert.match(store, /WHERE cb\.review_status=\$approved/);
  assert.match(store, /SELECT id FROM card_backs WHERE owner_player_id=\$playerId/);
  assert.match(models, /bool PubliclyListed,[\s\S]*string ReviewStatus,[\s\S]*string ReviewReason/);
});

test("审核协议由服务端管理员权限保护并支持通过或未通过", () => {
  assert.match(bridge, /case "MsgCardBackReviewQueue": OnCardBackReviewQueue/);
  assert.match(bridge, /case "MsgReviewCardBack": OnReviewCardBack/);
  assert.match(bridge, /AdministratorPolicy\.IsAuthorized\(s\.Account\)/);
  assert.match(bridge, /ReviewCardBack\([\s\S]*approved/);
  assert.match(protocol, /requestCardBackReviewQueue\(\)/);
  assert.match(protocol, /reviewCardBack\(cardBackId: string, approved: boolean, reason\?: string\)/);
});

test("管理员拥有独立审核页、默认安全理由和移动端可点击入口", () => {
  assert.match(panel, /DEFAULT_CARD_BACK_REJECTION_REASON/);
  assert.match(panel, /禁止使用真人人像、个人照片/);
  assert.match(panel, /data-testid="card-back-review"/);
  assert.match(panel, /processing \? "提交中…" : "通过"/);
  assert.match(panel, /processing \? "提交中…" : "未通过"/);
  assert.match(panel, /min-h-11/);
  assert.match(main, /type View =[\s\S]*"cardBackReview"/);
  assert.match(main, /maintenance\.canManage && \(/);
  assert.match(main, /repeat\(\$\{mobileNavItems\.length\}, minmax\(44px, 1fr\)\)/);
});
