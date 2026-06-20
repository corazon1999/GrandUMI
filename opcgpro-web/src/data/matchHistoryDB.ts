/**
 * matchHistoryDB.ts — 浏览器本地对局历史存储（路线二）
 *
 * 用 IndexedDB 保存本账号每局收到的服务端快照流，用于首页「战绩」列表与回放。
 * 分两个 objectStore：
 *   - "meta"      : 轻量元信息（列表用，getAll 不会拖出沉重的快照）
 *   - "snapshots" : 完整 MsgGameState[] 快照流（仅回放时按 id 取）
 *
 * 纯本地存储：清浏览器数据 / 换设备即丢失（路线二固有取舍）。
 */

import type { MsgGameState } from "@/types/net";

const DB_NAME = "grandumi-history";
const DB_VERSION = 1;
const STORE_META = "meta";
const STORE_SNAP = "snapshots";

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
  gameOverReason: string;
  turnCount: number;
  snapshotCount: number;
}

interface SnapshotRecord {
  id: string;
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

/** 保存一局（meta + snapshots 同事务写入），并裁剪到上限。 */
export async function saveMatch(meta: MatchMeta, snapshots: MsgGameState[]): Promise<void> {
  const db = await openDB();
  await new Promise<void>((resolve, reject) => {
    const tx = db.transaction([STORE_META, STORE_SNAP], "readwrite");
    tx.objectStore(STORE_META).put(meta);
    tx.objectStore(STORE_SNAP).put({ id: meta.id, snapshots } as SnapshotRecord);
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
  const tx = db.transaction(STORE_SNAP, "readonly");
  const rec = await reqToPromise(tx.objectStore(STORE_SNAP).get(id) as IDBRequest<SnapshotRecord | undefined>);
  return rec?.snapshots ?? null;
}

/** 删除一局（meta + snapshots）。 */
export async function deleteMatch(id: string): Promise<void> {
  const db = await openDB();
  await new Promise<void>((resolve, reject) => {
    const tx = db.transaction([STORE_META, STORE_SNAP], "readwrite");
    tx.objectStore(STORE_META).delete(id);
    tx.objectStore(STORE_SNAP).delete(id);
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
  });
}

/** 清空全部历史。 */
export async function clearAll(): Promise<void> {
  const db = await openDB();
  await new Promise<void>((resolve, reject) => {
    const tx = db.transaction([STORE_META, STORE_SNAP], "readwrite");
    tx.objectStore(STORE_META).clear();
    tx.objectStore(STORE_SNAP).clear();
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
