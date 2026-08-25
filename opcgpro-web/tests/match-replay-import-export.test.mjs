import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import {
  MAX_REPLAY_FILE_BYTES,
  MAX_REPLAY_SNAPSHOTS,
  REPLAY_FILE_FORMAT,
  REPLAY_FILE_VERSION,
  ReplayFileError,
  createImportedMatchMeta,
  createReplayDocument,
  createReplayFilename,
  parseReplayText,
  serializeReplayDocument,
  validateReplayDocument,
  validateReplayFileSize,
} from "../src/data/matchReplayFile.ts";

function player(name, leaderNumber) {
  return {
    name,
    leaderNumber,
    handCardNumbers: [],
    trashNumbers: [],
    fieldCards: [],
  };
}

function snapshot(tick, isGameOver = false) {
  return {
    proto: "MsgGameState",
    tick,
    my: player("我方", "OP01-001"),
    opponent: player("对手", "OP02-001"),
    phase: "Main",
    currentTurn: true,
    turnCount: tick + 1,
    isGameOver,
    winnerIsMe: isGameOver,
    viewerKind: "player",
    // 未知/后加字段必须原样往返，版本内不能因校验白名单而丢失。
    futureOptionalField: { enabled: true },
  };
}

function meta(overrides = {}) {
  return {
    id: "1720000000000_对手/测试",
    startedAt: 1_720_000_000_000,
    myName: "我方",
    opponentName: "对手:测试",
    myLeader: "OP01-001",
    opponentLeader: "OP02-001",
    winnerIsMe: true,
    gameOverReason: "对手生命耗尽",
    turnCount: 2,
    snapshotCount: 2,
    ...overrides,
  };
}

function document(overrides = {}) {
  return {
    format: REPLAY_FILE_FORMAT,
    version: REPLAY_FILE_VERSION,
    exportedAt: "2026-08-26T08:00:00.000Z",
    meta: meta(),
    snapshots: [snapshot(0), snapshot(1, true)],
    ...overrides,
  };
}

test("版本化回放文件可完成 meta + 完整 snapshots 往返", () => {
  const snapshots = [snapshot(0), snapshot(1, true)];
  const created = createReplayDocument(
    meta({ snapshotCount: 999 }),
    snapshots,
    "2026-08-26T08:00:00.000Z",
  );
  const parsed = parseReplayText(serializeReplayDocument(created));

  assert.equal(parsed.format, "grandumi-replay");
  assert.equal(parsed.version, 1);
  assert.equal(parsed.meta.snapshotCount, snapshots.length);
  assert.deepEqual(parsed.snapshots, snapshots);
  assert.deepEqual(parsed.snapshots[0].futureOptionalField, { enabled: true });
});

test("文件名安全可读且不包含系统非法字符", () => {
  const filename = createReplayFilename(meta());
  assert.match(filename, /^GrandUMI-回放-\d{8}-\d{4}-OP01-001-vs-OP02-001\.json$/);
  assert.doesNotMatch(filename, /[<>:"/\\|?*]/);
});

test("坏 JSON、错误标识、未知版本和损坏的基础结构均给出中文错误", () => {
  assert.throws(() => parseReplayText("{坏"), (error) => {
    assert.ok(error instanceof ReplayFileError);
    assert.match(error.message, /JSON 内容损坏/);
    return true;
  });
  assert.throws(
    () => validateReplayDocument(document({ format: "other-replay" })),
    /文件标识不正确/,
  );
  assert.throws(
    () => validateReplayDocument(document({ version: 2 })),
    /不支持该文件版本/,
  );
  assert.throws(
    () => validateReplayDocument(document({ meta: meta({ snapshotCount: 1 }) })),
    /snapshotCount 与 snapshots 数量不一致/,
  );
  assert.throws(
    () => validateReplayDocument(document({ snapshots: [snapshot(1), snapshot(1, true)] })),
    /tick 必须严格递增/,
  );
  assert.throws(
    () => validateReplayDocument(document({ snapshots: [snapshot(0), snapshot(1, false)] })),
    /最后一帧不是已结束对局/,
  );
  assert.throws(
    () => validateReplayDocument(document({
      snapshots: [
        { ...snapshot(0), my: { ...player("我方", "OP01-001"), handCardNumbers: "错误" } },
        snapshot(1, true),
      ],
    })),
    /handCardNumbers 不是有效数组/,
  );
});

test("文件体积和快照数量均有独立上限", () => {
  assert.doesNotThrow(() => validateReplayFileSize(MAX_REPLAY_FILE_BYTES));
  assert.throws(
    () => validateReplayFileSize(MAX_REPLAY_FILE_BYTES + 1),
    /文件超过 64 MiB 上限/,
  );
  assert.throws(
    () => validateReplayDocument(document({
      meta: meta({ snapshotCount: MAX_REPLAY_SNAPSHOTS }),
      snapshots: Array(MAX_REPLAY_SNAPSHOTS + 1).fill(snapshot(0)),
    })),
    new RegExp(`快照数量超过 ${MAX_REPLAY_SNAPSHOTS} 帧上限`),
  );
});

test("每次导入生成新本地 ID，冲突时稳定递增且不改写源元信息", () => {
  const source = meta();
  const firstPrefix = "1720000000000_对手_测试__import_2000000000000";
  const imported = createImportedMatchMeta(
    source,
    [source.id, firstPrefix, `${firstPrefix}_2`],
    2_000_000_000_000,
  );

  assert.equal(imported.id, `${firstPrefix}_3`);
  assert.equal(imported.importedAt, 2_000_000_000_000);
  assert.equal(source.id, "1720000000000_对手/测试");
  assert.notEqual(createImportedMatchMeta(source, [], 2_000_000_000_001).id, source.id);
});

test("旧元信息和旧快照缺少后来新增的可选字段时仍可导入", () => {
  const oldSnapshot = snapshot(8, true);
  delete oldSnapshot.viewerKind;
  delete oldSnapshot.futureOptionalField;
  const oldMeta = meta({ snapshotCount: 1 });
  delete oldMeta.isDraw;
  delete oldMeta.diceWinnerIsMe;
  delete oldMeta.isFirstPlayer;
  delete oldMeta.importedAt;

  const parsed = validateReplayDocument(document({ meta: oldMeta, snapshots: [oldSnapshot] }));
  assert.equal(parsed.meta.isDraw, undefined);
  assert.equal(parsed.meta.importedAt, undefined);
  assert.equal(parsed.snapshots[0].viewerKind, undefined);
});

test("IndexedDB 导入在一次 add 事务中写 meta 与分块，并保留旧整块读取兼容", async () => {
  const source = await readFile(new URL("../src/data/matchHistoryDB.ts", import.meta.url), "utf8");
  const validationIndex = source.indexOf("const replay = validateReplayDocument(value)");
  const writeTransactionIndex = source.indexOf(
    'db.transaction([STORE_META, STORE_CHUNKS], "readwrite")',
  );

  assert.ok(validationIndex >= 0 && validationIndex < writeTransactionIndex);
  assert.match(source, /objectStore\(STORE_META\)\.add\(meta\)/);
  assert.match(source, /chunks\.add\(\{/);
  assert.match(source, /tx\.onabort = \(\) => reject/);
  assert.match(source, /Number\.isFinite\(a\.importedAt\) \? a\.importedAt! : a\.startedAt/);
  assert.match(source, /if \(metas\.length <= MAX_MATCHES\) return/);
  assert.match(source, /return legacy\?\.snapshots \?\? null/);
});

test("历史面板接好重复选择、可访问反馈和桌面/手机 44px 操作区", async () => {
  const source = await readFile(
    new URL("../src/components/home/HistoryPanel.tsx", import.meta.url),
    "utf8",
  );

  assert.match(source, /accept="\.json,application\/json"/);
  assert.ok((source.match(/\.value = ""/g)?.length ?? 0) >= 2);
  assert.match(source, /validateReplayFileSize\(file\.size\)/);
  assert.match(source, /parseReplayText\(await file\.text\(\)\)/);
  assert.match(source, /await importReplayDocument\(replay\)/);
  assert.match(source, /createReplayDocument\(meta, snapshots\)/);
  assert.match(source, /serializeReplayDocument\(replay\)/);
  assert.match(source, /aria-live="polite"/);
  assert.match(source, /role=\{feedback\.type === "error" \? "alert" : "status"\}/);
  assert.match(source, /flex flex-wrap items-start gap-3/);
  assert.match(source, /className="flex min-h-11 min-w-0 flex-1/);
  assert.match(source, /className="flex h-11 min-w-11 shrink-0/);
  assert.match(source, /className="flex h-11 w-11 shrink-0/);
});
