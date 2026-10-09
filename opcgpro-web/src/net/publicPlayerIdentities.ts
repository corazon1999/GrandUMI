import { NetManager } from "./NetManager";
import type { MsgPublicPlayerIdentities } from "@/types/net";

const visibleNames = new Map<string, number>();
const pending = new Set<string>();
let batchTimer: ReturnType<typeof setTimeout> | undefined;
let refreshTimer: ReturnType<typeof setInterval> | undefined;

function flush() {
  batchTimer = undefined;
  const names = [...pending].filter((name) => visibleNames.has(name)).slice(0, 40);
  if (!names.length) { pending.clear(); return; }
  if (NetManager.send({ proto: "MsgPublicPlayerIdentities", names } as MsgPublicPlayerIdentities)) {
    for (const name of names) pending.delete(name);
  }
  if (pending.size) batchTimer = setTimeout(flush, 600);
}

function enqueue(name: string) {
  pending.add(name);
  batchTimer ??= setTimeout(flush, 80);
}

/** 同屏昵称合并查询；重连后重新订阅，定时补偿漏掉的佩戴更新。 */
export function watchPublicPlayerIdentity(name: string): () => void {
  if (!name.trim() || name.length > 32) return () => {};
  const count = visibleNames.get(name) ?? 0;
  visibleNames.set(name, count + 1);
  if (!count) enqueue(name);
  refreshTimer ??= setInterval(() => { for (const name of visibleNames.keys()) enqueue(name); }, 30_000);
  return () => {
    const remaining = (visibleNames.get(name) ?? 1) - 1;
    if (remaining > 0) visibleNames.set(name, remaining);
    else { visibleNames.delete(name); pending.delete(name); }
    if (!visibleNames.size) {
      clearInterval(refreshTimer); refreshTimer = undefined;
      clearTimeout(batchTimer); batchTimer = undefined;
    }
  };
}
