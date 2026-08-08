import type { MsgGameState, ReplayHandFrameSnapshot } from "@/types/net";

function findReplayHandTimeline(snapshots: readonly MsgGameState[]): ReplayHandFrameSnapshot[] | null {
  for (let i = snapshots.length - 1; i >= 0; i--) {
    const timeline = snapshots[i].replayHands;
    if (timeline?.length) return [...timeline].sort((a, b) => a.tick - b.tick);
  }
  return null;
}

/**
 * 将终局快照携带的压缩手牌时间线合并回每一帧。
 * 旧回放没有时间线时保持原样，由手牌组件继续显示对手卡背。
 */
export function revealReplayHands(snapshots: readonly MsgGameState[]): MsgGameState[] {
  const timeline = findReplayHandTimeline(snapshots);
  if (!timeline) return [...snapshots];

  let timelineIndex = -1;
  return snapshots.map((snapshot) => {
    while (
      timelineIndex + 1 < timeline.length
      && timeline[timelineIndex + 1].tick <= snapshot.tick
    ) {
      timelineIndex++;
    }

    if (timelineIndex < 0) return snapshot;
    const frame = timeline[timelineIndex];
    return {
      ...snapshot,
      my: {
        ...snapshot.my,
        handCardNumbers: [...frame.myCardNumbers],
        handCount: frame.myCardNumbers.length,
      },
      opponent: {
        ...snapshot.opponent,
        handCardNumbers: [...frame.opponentCardNumbers],
        handCount: frame.opponentCardNumbers.length,
      },
    };
  });
}
