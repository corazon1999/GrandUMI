import { useEffect, useState } from "react";
import { remainingSecondsFromServer } from "@/lib/serverCountdown.mjs";

function monotonicNow(): number {
  return typeof performance === "undefined" ? 0 : performance.now();
}

/** 根据最近一份服务端快照，用单调时钟推进倒计时。 */
export function useServerCountdown(
  deadlineUtc: string | null,
  serverNowUtc: string | null,
  active: boolean,
  fallbackSeconds = 60,
): number {
  const [anchor, setAnchor] = useState(() => ({ serverNowUtc, receivedAt: monotonicNow() }));
  const [now, setNow] = useState(() => monotonicNow());

  useEffect(() => {
    const receivedAt = monotonicNow();
    setAnchor({ serverNowUtc, receivedAt });
    setNow(receivedAt);
  }, [serverNowUtc]);

  useEffect(() => {
    if (!active) return;
    setNow(monotonicNow());
    const timer = window.setInterval(() => setNow(monotonicNow()), 250);
    return () => window.clearInterval(timer);
  }, [active]);

  const anchorMatchesSnapshot = anchor.serverNowUtc === serverNowUtc;
  return remainingSecondsFromServer(
    deadlineUtc,
    serverNowUtc,
    anchorMatchesSnapshot ? Math.max(0, now - anchor.receivedAt) : 0,
    fallbackSeconds,
  );
}
