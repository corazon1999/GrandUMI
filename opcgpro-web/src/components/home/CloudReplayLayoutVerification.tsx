"use client";

import type { CloudReplayListItem } from "@/types/net";
import CloudReplayPanel from "./CloudReplayPanel";
import LayoutPreviewFrame from "./LayoutPreviewFrame";

const SAMPLE_REPLAYS: CloudReplayListItem[] = [
  {
    replayId: "layout-sample-001",
    startedAt: Date.UTC(2026, 7, 31, 10, 15),
    completedAt: Date.UTC(2026, 7, 31, 10, 34),
    myName: "路飞",
    opponentName: "移动端长昵称验证玩家",
    myLeader: "OP01-001",
    opponentLeader: "OP05-060",
    winnerIsMe: true,
    isDraw: false,
    gameOverReason: "LifeZero",
    turnCount: 11,
    matchKind: "Ranked",
    bookmarked: true,
    shared: false,
    sharePolicy: "masked",
    feedbackCount: 2,
    sizeBytes: 1_734_912,
    runtimeArtifactId: "layout-verification-runtime",
  },
  {
    replayId: "layout-sample-002",
    startedAt: Date.UTC(2026, 7, 30, 8, 5),
    completedAt: Date.UTC(2026, 7, 30, 8, 22),
    myName: "索隆",
    opponentName: "娜美",
    myLeader: "OP06-020",
    opponentLeader: "OP02-026",
    winnerIsMe: false,
    isDraw: false,
    gameOverReason: "DeckOut",
    turnCount: 9,
    matchKind: "CasualStandard",
    bookmarked: false,
    shared: true,
    sharePolicy: "final_hands",
    feedbackCount: 0,
    sizeBytes: 1_204_224,
    runtimeArtifactId: "layout-verification-runtime",
  },
];

export default function CloudReplayLayoutVerification() {
  return (
    <LayoutPreviewFrame mode="desktop">
      <main data-cloud-replay-layout-verification className="h-full min-h-0 overflow-hidden bg-gray-950">
        <CloudReplayPanel onShowLocal={() => {}} previewItems={SAMPLE_REPLAYS} />
      </main>
    </LayoutPreviewFrame>
  );
}
