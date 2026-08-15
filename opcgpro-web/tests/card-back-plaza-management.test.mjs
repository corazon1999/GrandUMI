import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const [source, cardBack, protocol, store] = await Promise.all([
  readFile(new URL("../src/components/home/CardBackPlazaPanel.tsx", import.meta.url), "utf8"),
  readFile(new URL("../src/components/ui/CardBack.tsx", import.meta.url), "utf8"),
  readFile(new URL("../src/net/HomeProtocol.ts", import.meta.url), "utf8"),
  readFile(new URL("../src/store/netStore.ts", import.meta.url), "utf8"),
]);

test("卡背广场提供热门与我的投稿两个管理视图", () => {
  assert.match(source, /type GalleryView = "popular" \| "mine"/);
  assert.match(source, /role="tablist" aria-label="卡背广场分类"/);
  assert.match(source, />\s*热门卡背\s*</);
  assert.match(source, /我发布的卡背/);
  assert.match(source, /galleryView === "mine" \? ownedCardBacks : approvedCardBacks/);
  assert.match(source, /item\.reviewStatus === "approved" && item\.publiclyListed/);
  assert.match(source, /全部已通过审核的卡背均可浏览/);
  assert.match(source, /已显示 \$\{approvedCardBacks\.length\} \/ 共 \$\{galleryTotal\} 款/);
});

test("热门卡背使用游标自动续页并保留手动加载入口", () => {
  assert.match(source, /loadMoreSentinelRef/);
  assert.match(source, /rootMargin: "600px 0px"/);
  assert.match(source, /HomeRequest\.requestCardBackGallery\(galleryNextCursor\)/);
  assert.match(source, /正在加载更多…/);
  assert.match(protocol, /pageSize: 40/);
  assert.match(protocol, /append: Boolean\(msg\.cursor\)/);
  assert.match(store, /cardBackGalleryNextCursor/);
  assert.match(store, /seen = new Set<string>/);
});

test("卡背图片只在接近可视区时加载", () => {
  assert.match(source, /<CardBack cardBackId=\{item\.id\} decorative lazy \/>/);
  assert.match(cardBack, /new IntersectionObserver/);
  assert.match(cardBack, /rootMargin: "500px 0px"/);
  assert.match(cardBack, /loading=\{lazy \? "lazy" : "eager"\}/);
  assert.match(cardBack, /decoding="async"/);
});

test("点赞回包只更新对应卡背而不覆盖整个广场", () => {
  assert.match(protocol, /case "MsgLikeCardBack"/);
  assert.match(protocol, /updateCardBackGalleryItem\(msg\.item\)/);
  assert.match(store, /current\.id === item\.id \? item : current/);
});

test("本人投稿展示待审核与未通过状态且只有已通过卡背可互动", () => {
  assert.match(source, /item\.reviewStatus === "pending" \? "待审核" : "未通过"/);
  assert.match(source, /item\.reviewStatus === "rejected" && item\.reviewReason/);
  assert.match(source, /disabled=\{active \|\| !approved\}/);
  assert.match(source, /disabled=\{!approved\}/);
});

test("普通用户只能管理自己的投稿，管理员可在热门视图删除已发布卡背", () => {
  assert.match(source, /const canManage = useNetStore\(\(state\) => state\.maintenance\.canManage\)/);
  assert.match(source, /const canDeleteOwned = galleryView === "mine" && item\.owned/);
  assert.match(source, /const canAdminDelete = galleryView === "popular" && canManage && approved && item\.publiclyListed/);
  assert.match(source, /\{\(canDeleteOwned \|\| canAdminDelete\) && \(/);
  assert.match(source, /管理员删除/);
  assert.match(source, /HomeRequest\.deleteCardBack\(cardBackId\)/);
});

test("删除按钮需要不可恢复的二次确认", () => {
  assert.match(source, /window\.confirm\(t\(confirmation\)\)/);
  assert.match(source, /确定以管理员身份删除已发布卡背/);
  assert.match(source, /删除后无法恢复/);
});

test("卡背广场请求超时后停止无限加载并允许手动重试", () => {
  assert.match(source, /const GALLERY_TIMEOUT_MS = 8_000/);
  assert.match(source, /setGalleryTimedOut\(true\)/);
  assert.match(source, /卡背广场响应超时，请检查当前线路后重试。/);
  assert.match(source, /onClick=\{requestGallery\}/);
  assert.match(source, />\s*重试\s*</);
});
