import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import {
  INITIAL_RANK_SNAPSHOT_REQUEST_STATE,
  acceptRankSnapshot,
  beginRankSnapshotRequest,
  failRankSnapshotRequest,
  isRankSnapshotStale,
  shouldReplaceRankProfile,
  transitionRankSnapshotSeason,
} from "../src/lib/rankSnapshotState.ts";

test("排位榜请求超时后可重试，迟到的旧请求失败不覆盖新请求", () => {
  const loading1 = beginRankSnapshotRequest(INITIAL_RANK_SNAPSHOT_REQUEST_STATE, "request-1");
  const timedOut = failRankSnapshotRequest(loading1, "request-1", "加载超时");
  assert.equal(timedOut.phase, "error");
  assert.equal(timedOut.retryable, true);

  const loading2 = beginRankSnapshotRequest(timedOut, "request-2");
  const afterLateFailure = failRankSnapshotRequest(loading2, "request-1", "迟到错误");
  assert.equal(afterLateFailure, loading2);
  assert.equal(afterLateFailure.phase, "loading");
  assert.equal(afterLateFailure.requestId, "request-2");

  const succeeded = acceptRankSnapshot(loading2, {
    requestId: "request-2",
    seasonId: "S1",
    snapshotVersion: 1,
    generatedAtUtc: "2026-08-26T10:00:00.000Z",
  }).state;
  const afterFailureArrivesPostSuccess = failRankSnapshotRequest(
    succeeded,
    "request-1",
    "成功后的迟到错误",
  );
  assert.equal(afterFailureArrivesPostSuccess, succeeded);
  assert.equal(afterFailureArrivesPostSuccess.phase, "success");
});

test("排位榜只接受更新版本，乱序回包不覆盖新榜单也不结束新请求", () => {
  const first = acceptRankSnapshot(INITIAL_RANK_SNAPSHOT_REQUEST_STATE, {
    seasonId: "S1",
    snapshotVersion: 10,
    generatedAtUtc: "2026-08-26T10:00:00.000Z",
  });
  assert.equal(first.replacePublicSnapshot, true);

  const loading = beginRankSnapshotRequest(first.state, "request-new");
  const late = acceptRankSnapshot(loading, {
    requestId: "request-old",
    seasonId: "S1",
    snapshotVersion: 9,
    generatedAtUtc: "2026-08-26T09:59:45.000Z",
  });
  assert.equal(late.replacePublicSnapshot, false);
  assert.equal(late.state.phase, "loading");
  assert.equal(late.state.requestId, "request-new");
  assert.equal(late.state.snapshotVersion, 10);

  const current = acceptRankSnapshot(late.state, {
    requestId: "request-new",
    seasonId: "S1",
    snapshotVersion: 11,
    generatedAtUtc: "2026-08-26T10:00:15.000Z",
  });
  assert.equal(current.replacePublicSnapshot, true);
  assert.equal(current.state.phase, "success");
  assert.equal(current.state.snapshotVersion, 11);
});

test("新赛季与陈旧快照状态按边界处理", () => {
  const oldSeason = acceptRankSnapshot(INITIAL_RANK_SNAPSHOT_REQUEST_STATE, {
    seasonId: "S1",
    snapshotVersion: 20,
    generatedAtUtc: "2026-08-26T10:00:00.000Z",
  }).state;
  const newSeason = acceptRankSnapshot(oldSeason, {
    seasonId: "S2",
    snapshotVersion: 21,
    generatedAtUtc: "2026-10-05T00:00:00.000Z",
  });
  assert.equal(newSeason.replacePublicSnapshot, true);
  assert.equal(newSeason.state.seasonId, "S2");

  const lateOldSeason = acceptRankSnapshot(newSeason.state, {
    requestId: "late-s1",
    seasonId: "S1",
    snapshotVersion: 22,
    generatedAtUtc: "2026-08-26T10:01:00.000Z",
  });
  assert.equal(lateOldSeason.replacePublicSnapshot, false);
  assert.equal(lateOldSeason.state.seasonId, "S2");
  assert.equal(lateOldSeason.state.snapshotVersion, 21);

  assert.equal(isRankSnapshotStale("2026-08-26T10:00:00.000Z", Date.parse("2026-08-26T10:00:44.000Z")), false);
  assert.equal(isRankSnapshotStale("2026-08-26T10:00:00.000Z", Date.parse("2026-08-26T10:00:46.000Z")), true);
});

test("实时资料切换到新赛季时清空旧榜单版本且拒绝旧赛季资料回放", () => {
  const oldSeason = acceptRankSnapshot(INITIAL_RANK_SNAPSHOT_REQUEST_STATE, {
    seasonId: "S1",
    snapshotVersion: 30,
    generatedAtUtc: "2026-08-26T10:00:00.000Z",
  }).state;
  const transitioned = transitionRankSnapshotSeason(oldSeason, "S2");

  assert.equal(transitioned.clearPublicSnapshot, true);
  assert.equal(transitioned.state.seasonId, "S2");
  assert.equal(transitioned.state.snapshotVersion, null);
  assert.equal(transitioned.state.generatedAtUtc, null);
  assert.equal(shouldReplaceRankProfile({ seasonId: "S1", games: 20 }, { seasonId: "S2", games: 0 }), true);
  assert.equal(shouldReplaceRankProfile({ seasonId: "S2", games: 0 }, { seasonId: "S1", games: 20 }), false);
});

test("同赛季个人资料只允许阵营重置回包显式降低场次", () => {
  const current = { seasonId: "S2", games: 12 };
  const reset = { seasonId: "S2", games: 0 };
  assert.equal(shouldReplaceRankProfile(current, reset), false);
  assert.equal(shouldReplaceRankProfile(current, reset, true), true);
});

test("协议层实际接入 8 秒超时、发送失败和 44px 重试按钮", async () => {
  const [protocol, panel, store] = await Promise.all([
    readFile(new URL("../src/net/HomeProtocol.ts", import.meta.url), "utf8"),
    readFile(new URL("../src/components/home/LeaderLeaderboardPanel.tsx", import.meta.url), "utf8"),
    readFile(new URL("../src/store/netStore.ts", import.meta.url), "utf8"),
  ]);
  assert.match(protocol, /RANK_SNAPSHOT_REQUEST_TIMEOUT_MS = 8_000/);
  assert.match(protocol, /if \(!sent\)[\s\S]*failRankSnapshotRequest/);
  assert.match(protocol, /setTimeout\([\s\S]*排位榜加载超时/);
  assert.match(protocol, /eventBus\.on\("close"[\s\S]*failPendingRankSnapshotRequests/);
  assert.match(protocol, /allowSameSeasonProfileRegression: true/);
  assert.match(store, /transitionRankSnapshotSeason[\s\S]*rankLeaderboards:[\s\S]*\[mode\]: \[\]/);
  assert.match(panel, /重试加载[\s\S]*|min-h-11 min-w-11/);
  assert.match(panel, /min-h-11 min-w-11[\s\S]*重试/);
});
