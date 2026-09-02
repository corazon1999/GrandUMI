"use client";

import { useRef } from "react";
import { useNetStore } from "@/store/netStore";
import AdminPanel from "./AdminPanel";
import LayoutPreviewFrame from "./LayoutPreviewFrame";

const now = Date.UTC(2026, 8, 2, 9, 0);

export default function AdminPanelLayoutVerification() {
  const initialized = useRef(false);
  if (!initialized.current) {
    useNetStore.setState({
      account: "layout_admin",
      playerName: "布局验证管理员",
      connState: "disconnected",
      maintenance: { enabled: false, activeRoomCount: 3, startedAt: null, canManage: true },
      cardBackReviewQueue: [],
      adminOperations: {
        currentCommit: "1234567890abcdef",
        deploymentAvailable: false,
        onlineCount: 28,
        peaks7: Array.from({ length: 7 }, (_, index) => ({ date: `2026-08-${27 + index}`, peak: 30 + index })),
        peaks30: [],
        dailyActive7: Array.from({ length: 7 }, (_, index) => ({ date: `2026-08-${27 + index}`, count: 80 + index * 3 })),
        dailyActive30: [],
        playerTrafficUpdatedAt: now,
        matches7: Array.from({ length: 7 }, (_, index) => ({ date: `2026-08-${27 + index}`, count: 40 + index * 2 })),
        matches30: [],
        matchesUpdatedAt: now,
        storage: {
          totalBytes: 500 * 1024 ** 3,
          availableBytes: 320 * 1024 ** 3,
          healthy: true,
          reason: "空间充足",
          updatedAt: now,
          refreshIntervalHours: 3,
        },
        test: { environment: "test", state: "unavailable", message: "布局验证不会连接或写入真实服务。" },
        production: { environment: "production", state: "unavailable", message: "布局验证不会连接或写入真实服务。" },
      },
    });
    initialized.current = true;
  }

  return (
    <LayoutPreviewFrame mode="desktop">
      <main data-admin-panel-layout-verification className="h-full min-h-0 overflow-hidden bg-gray-950">
        <AdminPanel
          layoutVerification
          onOpenCardBackReview={() => undefined}
          onOpenPlayers={() => undefined}
          onReturnToLobby={() => undefined}
        />
      </main>
    </LayoutPreviewFrame>
  );
}
