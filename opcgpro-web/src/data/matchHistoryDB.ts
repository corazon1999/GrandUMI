/**
 * matchHistoryDB.ts — 浏览器本地对局历史存储（路线二）
 *
 * 用 IndexedDB 保存本账号每局收到的服务端快照流，用于首页「战绩」列表与回放。
 * 分两个 objectStore：
 *   - "meta"      : 轻量元信息（列表用，getAll 不会拖出沉重的快照）
 *   - "snapshots" : 旧版完整 MsgGameState[] 快照流（只读兼容）
 *   - "snapshotChunks" : 新版分块快照流，对局中持续写入，避免整局常驻内存
 *
 * 纯本地存储：清浏览器数据 / 换设备即丢失（路线二固有取舍）。
 */

import type { MsgGameState } from "@/types/net";

const DB_NAME = "grandumi-history";
const DB_VERSION = 2;
const STORE_META = "meta";
const STORE_SNAP = "snapshots";
const STORE_CHUNKS = "snapshotChunks";
const INDEX_CHUNKS_BY_MATCH = "byMatch";

/** 保留上限：超出后按开始时间删最旧 */
export const MAX_MATCHES = 30;

/** 列表元信息（轻量） */
export interface MatchMeta {
  id: string;
  startedAt: number;        // 开局时间戳（ms）
  myName: string;
  opponentName: string;
  myLeader: string;         // 我方领航卡号
  opponentLeader: string;   // 对手领航卡号
  winnerIsMe: boolean;
  isDraw?: boolean;
  gameOverReason: string;
  turnCount: number;
  snapshotCount: number;
}

interface SnapshotRecord {
  id: string;
  snapshots: MsgGameState[];
}

interface SnapshotChunkRecord {
  matchId: string;
  chunkIndex: number;
  snapshots: MsgGameState[];
}

let dbPromise: Promise<IDBDatabase> | null = null;

function openDB(): Promise<IDBDatabase> {
  if (typeof indexedDB === "undefined") {
    return Promise.reject(new Error("IndexedDB 不可用"));
  }
  if (dbPromise) return dbPromise;
  dbPromise = new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, DB_VERSION);
    req.onupgradeneeded = () => {
      const db = req.result;
      if (!db.objectStoreNames.contains(STORE_META)) {
        db.createObjectStore(STORE_META, { keyPath: "id" });
      }
      if (!db.objectStoreNames.contains(STORE_SNAP)) {
        db.createObjectStore(STORE_SNAP, { keyPath: "id" });
      }
      if (!db.objectStoreNames.contains(STORE_CHUNKS)) {
        const chunks = db.createObjectStore(STORE_CHUNKS, { keyPath: ["matchId", "chunkIndex"] });
        chunks.createIndex(INDEX_CHUNKS_BY_MATCH, "matchId", { unique: false });
      }
    };
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
  return dbPromise;
}

function reqToPromise<T>(req: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

/** 兼容旧调用方式：把整局作为单个新版分块写入，再保存元信息。 */
export async function saveMatch(meta: MatchMeta, snapshots: MsgGameState[]): Promise<void> {
  await appendSnapshotChunk(meta.id, 0, snapshots);
  await saveMatchMeta(meta);
}

/** 对局进行中追加一个小快照分块。 */
export async function appendSnapshotChunk(
  matchId: string,
  chunkIndex: number,
  snapshots: MsgGameState[],
): Promise<void> {
  if (snapshots.length === 0) return;
  const db = await openDB();
  await new Promise<void>((resolve, reject) => {
    const tx = db.transaction(STORE_CHUNKS, "readwrite");
    tx.objectStore(STORE_CHUNKS).put({ matchId, chunkIndex, snapshots } as SnapshotChunkRecord);
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
  });
}

/** 对局结束后只写轻量元信息；快照已在过程中分块落盘。 */
export async function saveMatchMeta(meta: MatchMeta): Promise<void> {
  const db = await openDB();
  await new Promise<void>((resolve, reject) => {
    const tx = db.transaction([STORE_META, STORE_SNAP], "readwrite");
    tx.objectStore(STORE_META).put(meta);
    // 若同 ID 曾由旧接口保存，移除旧版整块，避免双份占用。
    tx.objectStore(STORE_SNAP).delete(meta.id);
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
  });
  await prune();
}

/** 列出全部元信息，按开始时间倒序（最新在前）。 */
export async function listMeta(): Promise<MatchMeta[]> {
  const db = await openDB();
  const tx = db.transaction(STORE_META, "readonly");
  const all = await reqToPromise(tx.objectStore(STORE_META).getAll() as IDBRequest<MatchMeta[]>);
  return all.sort((a, b) => b.startedAt - a.startedAt);
}

/** 取某局完整快照流。 */
export async function getSnapshots(id: string): Promise<MsgGameState[] | null> {
  const db = await openDB();
  const tx = db.transaction([STORE_SNAP, STORE_CHUNKS], "readonly");
  const legacyRequest = tx.objectStore(STORE_SNAP).get(id) as IDBRequest<SnapshotRecord | undefined>;
  const chunksRequest = tx.objectStore(STORE_CHUNKS)
    .index(INDEX_CHUNKS_BY_MATCH)
    .getAll(IDBKeyRange.only(id)) as IDBRequest<SnapshotChunkRecord[]>;
  const [legacy, chunks] = await Promise.all([
    reqToPromise(legacyRequest),
    reqToPromise(chunksRequest),
  ]);
  if (chunks.length > 0) {
    return chunks
      .sort((a, b) => a.chunkIndex - b.chunkIndex)
      .flatMap((chunk) => chunk.snapshots);
  }
  return legacy?.snapshots ?? null;
}

/** 删除一局（meta + snapshots）。 */
export async function deleteMatch(id: string): Promise<void> {
  const db = await openDB();
  await new Promise<void>((resolve, reject) => {
    const tx = db.transaction([STORE_META, STORE_SNAP, STORE_CHUNKS], "readwrite");
    tx.objectStore(STORE_META).delete(id);
    tx.objectStore(STORE_SNAP).delete(id);
    deleteChunksInTransaction(tx.objectStore(STORE_CHUNKS), id);
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
  });
}

/** 清空全部历史。 */
export async function clearAll(): Promise<void> {
  const db = await openDB();
  await new Promise<void>((resolve, reject) => {
    const tx = db.transaction([STORE_META, STORE_SNAP, STORE_CHUNKS], "readwrite");
    tx.objectStore(STORE_META).clear();
    tx.objectStore(STORE_SNAP).clear();
    tx.objectStore(STORE_CHUNKS).clear();
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
  });
}

/** 超过上限时删除最旧的若干局。 */
async function prune(): Promise<void> {
  const metas = await listMeta(); // 已按时间倒序
  if (metas.length <= MAX_MATCHES) return;
  const toDelete = metas.slice(MAX_MATCHES);
  for (const m of toDelete) {
    await deleteMatch(m.id).catch(() => {});
  }
}

function deleteChunksInTransaction(store: IDBObjectStore, matchId: string) {
  const request = store.index(INDEX_CHUNKS_BY_MATCH).openKeyCursor(IDBKeyRange.only(matchId));
  request.onsuccess = () => {
    const cursor = request.result;
    if (!cursor) return;
    store.delete(cursor.primaryKey);
    cursor.continue();
  };
}
