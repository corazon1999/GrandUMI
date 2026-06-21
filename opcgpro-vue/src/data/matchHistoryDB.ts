import type { MsgGameState } from "@/types/net";

const DB_NAME = "grandumi-history";
const DB_VERSION = 1;
const STORE_META = "meta";
const STORE_SNAP = "snapshots";

export const MAX_MATCHES = 30;

export interface MatchMeta {
  id: string;
  startedAt: number;
  myName: string;
  opponentName: string;
  myLeader: string;
  opponentLeader: string;
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

export async function listMeta(): Promise<MatchMeta[]> {
  const db = await openDB();
  const tx = db.transaction(STORE_META, "readonly");
  const all = await reqToPromise(tx.objectStore(STORE_META).getAll() as IDBRequest<MatchMeta[]>);
  return all.sort((a, b) => b.startedAt - a.startedAt);
}

export async function getSnapshots(id: string): Promise<MsgGameState[] | null> {
  const db = await openDB();
  const tx = db.transaction(STORE_SNAP, "readonly");
  const rec = await reqToPromise(tx.objectStore(STORE_SNAP).get(id) as IDBRequest<SnapshotRecord | undefined>);
  return rec?.snapshots ?? null;
}

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

async function prune(): Promise<void> {
  const metas = await listMeta();
  if (metas.length <= MAX_MATCHES) return;
  const toDelete = metas.slice(MAX_MATCHES);
  for (const m of toDelete) {
    await deleteMatch(m.id).catch(() => {});
  }
}
