"use client";

import { useEffect, useState } from "react";
import InactivityWarningOverlay from "@/components/game/InactivityWarningOverlay";
import ReconnectOverlay from "@/components/game/ReconnectOverlay";
import LayoutPreviewFrame from "@/components/home/LayoutPreviewFrame";
import { NetManager } from "@/net/NetManager";
import { useGameStore } from "@/store/gameStore";
import { useNetStore } from "@/store/netStore";
import type { ConnectionState } from "@/net/eventBus";

type VerificationView = "warning" | "reconnecting" | "recovering" | "failed";

const FIXTURE_CONNECTION_EPOCH = 9;

/** 仅供受环境变量保护的布局验证路由使用。 */
export default function InactivityRecoveryLayoutVerification({
  view,
  mobile,
}: {
  view: VerificationView;
  mobile: boolean;
}) {
  const [ready, setReady] = useState(false);

  useEffect(() => {
    NetManager.disconnect();
    const connState: ConnectionState = view === "warning" ? "connected" : view;
    const now = new Date();
    useGameStore.setState({
      inactivityActive: "my",
      inactivityWarningActive: true,
      inactivityLossRemainingMs: 158_000,
      inactivitySyncUtc: now.toISOString(),
      serverNowUtc: now.toISOString(),
      isGameOver: false,
      snapshotConnectionEpoch: view === "warning"
        ? FIXTURE_CONNECTION_EPOCH
        : FIXTURE_CONNECTION_EPOCH - 1,
    });
    useNetStore.setState({
      connState,
      connectionEpoch: FIXTURE_CONNECTION_EPOCH,
      reconnectCountdown: 3,
    });
    setReady(true);
  }, [view]);

  return (
    <LayoutPreviewFrame
      mode={mobile ? "mobile-landscape" : "desktop"}
      rotateQuarterTurn={mobile}
      edgeToEdge
    >
      <main
        data-inactivity-recovery-layout-verification={view}
        className="relative h-full w-full overflow-hidden bg-[#07111f]"
      >
        <div className="absolute inset-4 grid grid-cols-[12rem_1fr] gap-4 opacity-60">
          <aside className="rounded-xl border border-sky-300/20 bg-slate-950/80" />
          <section className="grid grid-rows-2 gap-4">
            <div className="rounded-xl border border-red-300/20 bg-red-950/30" />
            <div className="rounded-xl border border-sky-300/20 bg-sky-950/30" />
          </section>
        </div>
        {ready && (
          <>
            <InactivityWarningOverlay />
            <ReconnectOverlay />
          </>
        )}
      </main>
    </LayoutPreviewFrame>
  );
}
