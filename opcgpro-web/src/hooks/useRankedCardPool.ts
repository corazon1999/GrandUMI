"use client";

import { useEffect, useState } from "react";
import type { StandardRankedCardPool } from "@/types/net";
import { loadRankedCardPool, rankedCardPoolStorageKey, saveRankedCardPool } from "@/lib/rankedCardPool";

function browserStorage(): Storage | null {
  try { return window.localStorage; } catch { return null; }
}

/** 按账号恢复偏好；只在主动选择时写入，避免初次渲染覆盖已保存的环境。 */
export function useRankedCardPool(account: string) {
  const key = rankedCardPoolStorageKey(account);
  const [saved, setSaved] = useState<{ key: string | null; value: StandardRankedCardPool }>({ key: null, value: "real" });
  useEffect(() => {
    setSaved({ key, value: loadRankedCardPool(account, browserStorage()) });
  }, [account, key]);
  const ready = saved.key === key;
  const value = ready ? saved.value : "real";
  const select = (next: StandardRankedCardPool) => {
    setSaved({ key, value: next });
    saveRankedCardPool(account, next, browserStorage());
  };
  return { value, select, ready };
}
