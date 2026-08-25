/**
 * 对局回放文件格式与运行时校验。
 *
 * 文件格式有独立版本号；IndexedDB 的历史结构升级不会直接改变已导出的文件。
 * 校验只要求播放所需的稳定基础字段，允许旧快照缺少后来新增的可选字段。
 */

import type { MsgGameState } from "@/types/net";
import type { MatchMeta } from "./matchHistoryDB";

export const REPLAY_FILE_FORMAT = "grandumi-replay";
export const REPLAY_FILE_VERSION = 1 as const;
export const MAX_REPLAY_FILE_BYTES = 64 * 1024 * 1024;
export const MAX_REPLAY_SNAPSHOTS = 10_000;

const MAX_META_ID_LENGTH = 256;
const MAX_NAME_LENGTH = 160;
const MAX_LEADER_NUMBER_LENGTH = 80;
const MAX_REASON_LENGTH = 1_000;
const MAX_TURN_COUNT = 100_000;
const MAX_HAND_CARDS = 200;
const MAX_FIELD_CARDS = 100;
const MAX_TRASH_CARDS = 2_000;
const MAX_CARD_NUMBER_LENGTH = 100;

export interface ReplayFileV1 {
  format: typeof REPLAY_FILE_FORMAT;
  version: typeof REPLAY_FILE_VERSION;
  exportedAt: string;
  meta: MatchMeta;
  snapshots: MsgGameState[];
}

export class ReplayFileError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "ReplayFileError";
  }
}

function fail(detail: string): never {
  throw new ReplayFileError(`无法导入回放：${detail}`);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function requireString(
  value: unknown,
  label: string,
  maxLength: number,
  allowEmpty = false,
): string {
  if (typeof value !== "string" || (!allowEmpty && value.trim().length === 0)) {
    fail(`${label}不是有效文本`);
  }
  if (value.length > maxLength) fail(`${label}过长`);
  return value;
}

function requireBoolean(value: unknown, label: string): boolean {
  if (typeof value !== "boolean") fail(`${label}不是布尔值`);
  return value;
}

function requireInteger(value: unknown, label: string, min: number, max: number): number {
  if (!Number.isSafeInteger(value) || (value as number) < min || (value as number) > max) {
    fail(`${label}超出允许范围`);
  }
  return value as number;
}

function optionalBoolean(value: unknown, label: string): boolean | undefined {
  if (value === undefined) return undefined;
  return requireBoolean(value, label);
}

function optionalInteger(
  value: unknown,
  label: string,
  min: number,
  max: number,
): number | undefined {
  if (value === undefined) return undefined;
  return requireInteger(value, label, min, max);
}

function requireStringArray(
  value: unknown,
  label: string,
  maxItems: number,
): string[] {
  if (!Array.isArray(value) || value.length > maxItems) fail(`${label} 不是有效数组或数量过多`);
  for (let index = 0; index < value.length; index++) {
    requireString(value[index], `${label}[${index}]`, MAX_CARD_NUMBER_LENGTH, true);
  }
  return value as string[];
}

function validatePlayer(value: unknown, label: string): void {
  if (!isRecord(value)) fail(`${label}玩家数据缺失`);
  requireString(value.name, `${label}.name`, MAX_NAME_LENGTH, true);
  requireString(value.leaderNumber, `${label}.leaderNumber`, MAX_LEADER_NUMBER_LENGTH, true);
  requireStringArray(value.handCardNumbers, `${label}.handCardNumbers`, MAX_HAND_CARDS);
  requireStringArray(value.trashNumbers, `${label}.trashNumbers`, MAX_TRASH_CARDS);
  if (!Array.isArray(value.fieldCards) || value.fieldCards.length > MAX_FIELD_CARDS) {
    fail(`${label}.fieldCards 不是有效数组或数量过多`);
  }
  for (let index = 0; index < value.fieldCards.length; index++) {
    const card = value.fieldCards[index];
    if (!isRecord(card)) fail(`${label}.fieldCards[${index}] 不是有效卡牌`);
    requireString(card.id, `${label}.fieldCards[${index}].id`, MAX_META_ID_LENGTH, true);
    requireString(card.number, `${label}.fieldCards[${index}].number`, MAX_CARD_NUMBER_LENGTH, true);
  }
}

function validateSnapshot(value: unknown, index: number, previousTick: number): number {
  const label = `snapshots[${index}]`;
  if (!isRecord(value)) fail(`${label} 不是有效快照`);
  if (value.proto !== "MsgGameState") fail(`${label}.proto 必须为 MsgGameState`);
  const tick = requireInteger(value.tick, `${label}.tick`, 0, Number.MAX_SAFE_INTEGER);
  if (tick <= previousTick) fail(`${label}.tick 必须严格递增`);

  validatePlayer(value.my, `${label}.my`);
  validatePlayer(value.opponent, `${label}.opponent`);
  requireString(value.phase, `${label}.phase`, 100, true);
  requireBoolean(value.currentTurn, `${label}.currentTurn`);
  requireInteger(value.turnCount, `${label}.turnCount`, 0, MAX_TURN_COUNT);
  requireBoolean(value.isGameOver, `${label}.isGameOver`);
  requireBoolean(value.winnerIsMe, `${label}.winnerIsMe`);
  if (value.viewerKind !== undefined && value.viewerKind !== "player") {
    fail(`${label}.viewerKind 不是玩家回放`);
  }
  return tick;
}

function validateMeta(value: unknown, snapshotCount: number): MatchMeta {
  if (!isRecord(value)) fail("meta 缺失");
  const meta: MatchMeta = {
    id: requireString(value.id, "meta.id", MAX_META_ID_LENGTH),
    startedAt: requireInteger(value.startedAt, "meta.startedAt", 0, 4_102_444_800_000),
    myName: requireString(value.myName, "meta.myName", MAX_NAME_LENGTH, true),
    opponentName: requireString(value.opponentName, "meta.opponentName", MAX_NAME_LENGTH, true),
    myLeader: requireString(value.myLeader, "meta.myLeader", MAX_LEADER_NUMBER_LENGTH, true),
    opponentLeader: requireString(
      value.opponentLeader,
      "meta.opponentLeader",
      MAX_LEADER_NUMBER_LENGTH,
      true,
    ),
    winnerIsMe: requireBoolean(value.winnerIsMe, "meta.winnerIsMe"),
    gameOverReason: requireString(value.gameOverReason, "meta.gameOverReason", MAX_REASON_LENGTH, true),
    turnCount: requireInteger(value.turnCount, "meta.turnCount", 0, MAX_TURN_COUNT),
    snapshotCount: requireInteger(value.snapshotCount, "meta.snapshotCount", 1, MAX_REPLAY_SNAPSHOTS),
  };

  const isDraw = optionalBoolean(value.isDraw, "meta.isDraw");
  const diceWinnerIsMe = optionalBoolean(value.diceWinnerIsMe, "meta.diceWinnerIsMe");
  const isFirstPlayer = optionalBoolean(value.isFirstPlayer, "meta.isFirstPlayer");
  const importedAt = optionalInteger(value.importedAt, "meta.importedAt", 0, 4_102_444_800_000);
  if (isDraw !== undefined) meta.isDraw = isDraw;
  if (diceWinnerIsMe !== undefined) meta.diceWinnerIsMe = diceWinnerIsMe;
  if (isFirstPlayer !== undefined) meta.isFirstPlayer = isFirstPlayer;
  if (importedAt !== undefined) meta.importedAt = importedAt;

  if (meta.snapshotCount !== snapshotCount) {
    fail("meta.snapshotCount 与 snapshots 数量不一致");
  }
  return meta;
}

/** 先检查 File.size，可避免把明显超限的文件完整读入内存。 */
export function validateReplayFileSize(byteLength: number): void {
  if (!Number.isSafeInteger(byteLength) || byteLength < 0) fail("文件大小无效");
  if (byteLength > MAX_REPLAY_FILE_BYTES) {
    fail(`文件超过 ${MAX_REPLAY_FILE_BYTES / 1024 / 1024} MiB 上限`);
  }
}

/** 校验未知对象并返回剥离了非格式字段的顶层与元信息。快照未知字段原样保留以兼容演进。 */
export function validateReplayDocument(value: unknown): ReplayFileV1 {
  if (!isRecord(value)) fail("文件顶层不是对象");
  if (value.format !== REPLAY_FILE_FORMAT) fail("文件标识不正确");
  if (value.version !== REPLAY_FILE_VERSION) {
    fail(`不支持该文件版本（当前仅支持 v${REPLAY_FILE_VERSION}）`);
  }
  const exportedAt = requireString(value.exportedAt, "exportedAt", 64);
  if (!Number.isFinite(Date.parse(exportedAt))) fail("exportedAt 不是有效时间");
  if (!Array.isArray(value.snapshots)) fail("snapshots 不是数组");
  if (value.snapshots.length === 0) fail("回放不包含任何快照");
  if (value.snapshots.length > MAX_REPLAY_SNAPSHOTS) {
    fail(`快照数量超过 ${MAX_REPLAY_SNAPSHOTS} 帧上限`);
  }

  let previousTick = -1;
  for (let index = 0; index < value.snapshots.length; index++) {
    previousTick = validateSnapshot(value.snapshots[index], index, previousTick);
  }
  const lastSnapshot = value.snapshots[value.snapshots.length - 1] as Record<string, unknown>;
  if (lastSnapshot.isGameOver !== true) fail("最后一帧不是已结束对局");

  return {
    format: REPLAY_FILE_FORMAT,
    version: REPLAY_FILE_VERSION,
    exportedAt,
    meta: validateMeta(value.meta, value.snapshots.length),
    snapshots: value.snapshots as MsgGameState[],
  };
}

/** 从本地记录生成 v1 文档；实际帧数覆盖旧元信息中可能过时的 snapshotCount。 */
export function createReplayDocument(
  meta: MatchMeta,
  snapshots: MsgGameState[],
  exportedAt = new Date().toISOString(),
): ReplayFileV1 {
  return validateReplayDocument({
    format: REPLAY_FILE_FORMAT,
    version: REPLAY_FILE_VERSION,
    exportedAt,
    meta: { ...meta, snapshotCount: snapshots.length },
    snapshots,
  });
}

export function serializeReplayDocument(document: ReplayFileV1): string {
  const validated = validateReplayDocument(document);
  let text: string;
  try {
    text = JSON.stringify(validated);
  } catch {
    throw new ReplayFileError("无法导出回放：回放内容无法序列化");
  }
  const byteLength = new TextEncoder().encode(text).byteLength;
  if (byteLength > MAX_REPLAY_FILE_BYTES) {
    throw new ReplayFileError(
      `无法导出回放：文件超过 ${MAX_REPLAY_FILE_BYTES / 1024 / 1024} MiB 上限`,
    );
  }
  return text;
}

export function parseReplayText(text: string): ReplayFileV1 {
  validateReplayFileSize(new TextEncoder().encode(text).byteLength);
  let value: unknown;
  try {
    // 兼容少数编辑器写入的 UTF-8 BOM。
    value = JSON.parse(text.replace(/^\uFEFF/, ""));
  } catch {
    fail("JSON 内容损坏");
  }
  return validateReplayDocument(value);
}

/** 每次导入都创建独立本地 ID；同一文件可反复导入且绝不会覆盖原记录。 */
export function createImportedMatchMeta(
  source: MatchMeta,
  existingIds: Iterable<string>,
  importedAt = Date.now(),
): MatchMeta {
  const existing = new Set(existingIds);
  const safeSourceId = source.id
    .replace(/[\u0000-\u001f<>:"/\\|?*#%]+/g, "_")
    .replace(/\s+/g, "_")
    .replace(/^\.+|\.+$/g, "")
    .slice(0, 120) || "replay";
  const prefix = `${safeSourceId}__import_${Math.trunc(importedAt)}`;
  let id = prefix;
  let suffix = 2;
  while (existing.has(id)) id = `${prefix}_${suffix++}`;
  return {
    ...source,
    id,
    importedAt: Math.trunc(importedAt),
  };
}

function safeFilenamePart(value: string, fallback: string): string {
  const safe = value
    .normalize("NFKC")
    .replace(/[\u0000-\u001f<>:"/\\|?*]+/g, "-")
    .replace(/\s+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 32);
  return safe || fallback;
}

/** 文件名只包含可读对局信息，并移除 Windows/macOS 常见非法字符。 */
export function createReplayFilename(meta: MatchMeta): string {
  const date = new Date(meta.startedAt);
  const stamp = Number.isFinite(date.getTime())
    ? `${date.getFullYear()}${String(date.getMonth() + 1).padStart(2, "0")}${String(date.getDate()).padStart(2, "0")}-${String(date.getHours()).padStart(2, "0")}${String(date.getMinutes()).padStart(2, "0")}`
    : "unknown-time";
  const mine = safeFilenamePart(meta.myLeader || meta.myName, "我方");
  const opponent = safeFilenamePart(meta.opponentLeader || meta.opponentName, "对手");
  return `GrandUMI-回放-${stamp}-${mine}-vs-${opponent}.json`;
}
