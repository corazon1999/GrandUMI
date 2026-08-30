"use client";

import type { OperationsWorkbenchState } from "@/store/netStore";
import LayoutPreviewFrame from "./LayoutPreviewFrame";
import OperationsWorkbench from "./OperationsWorkbench";

const now = Date.UTC(2026, 7, 31, 12, 0);
const caseSummary = {
  caseId: "CASE-20260831-MOBILE-001",
  source: "player_report",
  category: "abuse",
  title: "移动端长标题验证：对局聊天与玩家举报统一处置",
  status: "investigating" as const,
  priority: "high" as const,
  reporterAccount: "layout_reporter",
  subjectAccount: "layout_subject_with_long_account",
  relatedAccount: null,
  roomId: "ROOM-LAYOUT-001",
  replayId: "REPLAY-LAYOUT-001",
  assignee: "operator_a",
  disposition: null,
  createdAt: now - 3_600_000,
  firstActionAt: now - 3_000_000,
  updatedAt: now - 60_000,
  evidenceCount: 2,
  activePenaltyCount: 1,
};

const PREVIEW_STATE: OperationsWorkbenchState = {
  cases: [
    caseSummary,
    {
      ...caseSummary,
      caseId: "CASE-20260831-QQ-002",
      source: "qq_bug",
      title: "QQ 机器人转入的普通问题草稿",
      status: "new",
      priority: "normal",
      subjectAccount: null,
      relatedAccount: "layout_player_2",
      evidenceCount: 1,
      activePenaltyCount: 0,
    },
  ],
  total: 2,
  metrics: { total: 2, awaitingFirstAction: 1, firstActionP90Ms: 642_000, byStatus: { new: 1, investigating: 1 } },
  selectedCase: {
    summary: caseSummary,
    description: "玩家举报对局聊天中出现持续骚扰，需要结合房间、回放与聊天原文进行统一处置。",
    externalEventId: "layout-event-001",
    appealText: null,
    evidence: [{ id: 1, type: "game_chat", payloadJson: JSON.stringify({ message: "布局验证用聊天证据", sender: "layout_subject_with_long_account" }), createdAt: now - 3_500_000, expiresAt: now + 30 * 86_400_000 }],
    events: [{ id: 1, eventType: "status_changed", fromStatus: "triaged", toStatus: "investigating", actorAccount: "operator_a", source: "web_admin", requestId: "layout-request", note: "已调取回放与聊天证据", createdAt: now - 3_000_000 }],
    penalties: [{ penaltyId: "PENALTY-LAYOUT-001", caseId: caseSummary.caseId, account: "layout_subject_with_long_account", kind: "mute", reason: "持续骚扰", operatorAccount: "operator_a", source: "web_admin", startsAt: now - 600_000, expiresAt: now + 86_400_000 }],
  },
  auditEntries: [{ id: 1, actorAccount: "operator_a", source: "web_admin", operation: "penalty_apply", target: "layout_subject_with_long_account", requestId: "layout-audit-request", result: "success", detailJson: JSON.stringify({ kind: "mute", durationHours: 24 }), createdAt: now - 600_000, previousHash: "0".repeat(64), eventHash: "a".repeat(64) }],
  auditChainValid: true,
  findings: [{ id: 7, scope: "display_name_directory", findingKey: "layout_subject_with_long_account", status: "open", severity: "warning", authoritativeJson: JSON.stringify({ displayName: "权威昵称" }), observedJson: JSON.stringify({ displayName: "旧昵称" }), repairAction: "sync_display_name", lastError: null, firstSeenAt: now - 7_200_000, lastSeenAt: now - 60_000, resolvedAt: null }],
  doctorSnapshot: {
    checkedAt: now,
    processed: 3,
    succeeded: 2,
    retried: 1,
    openFindings: 1,
    outboxCounts: { pending: 1, completed: 12 },
    schemas: [
      { name: "player-data", path: "D:/data/player-data.db", exists: true, healthy: true, integrity: "ok", userVersion: 7, migrationTables: ["schema_migrations"], sizeBytes: 1_048_576, lastWriteAt: now },
      { name: "shared-accounts", path: "D:/data/grandumi-shared/accounts.db", exists: true, healthy: true, integrity: "ok", userVersion: 4, migrationTables: ["schema_migrations"], sizeBytes: 524_288, lastWriteAt: now },
    ],
  },
  approval: null,
};

export default function OperationsWorkbenchLayoutVerification() {
  return (
    <LayoutPreviewFrame mode="desktop">
      <main data-operations-workbench-layout-verification className="h-full min-h-0 overflow-y-auto overflow-x-hidden bg-gray-950 p-3 pb-[max(0.75rem,var(--layout-safe-bottom,env(safe-area-inset-bottom)))]">
        <OperationsWorkbench previewState={PREVIEW_STATE} />
      </main>
    </LayoutPreviewFrame>
  );
}
