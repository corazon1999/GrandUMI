import type { MsgGameState, ReplayHandFrameSnapshot } from "@/types/net";

function findReplayHandTimeline(snapshots: readonly MsgGameState[]): ReplayHandFrameSnapshot[] | null {
  for (let i = snapshots.length - 1; i >= 0; i--) {
    const timeline = snapshots[i].replayHands;
    if (timeline?.length) return [...timeline].sort((a, b) => a.tick - b.tick);
  }
  return null;
}

/**
 * 将终局快照携带的压缩隐藏区时间线合并回每一帧。
 * 旧回放没有生命区时间线时保持原样，继续显示生命卡背。
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
    const myLife = frame.myLifeCardNumbers;
    const opponentLife = frame.opponentLifeCardNumbers;
    return {
      ...snapshot,
      my: {
        ...snapshot.my,
        handCardNumbers: [...frame.myCardNumbers],
        handCount: frame.myCardNumbers.length,
        ...(myLife
          ? {
              lifeNumbers: [...myLife],
              lifeCount: myLife.length,
              lifeFaceUp: myLife.map((number) => ({ faceUp: true, number })),
            }
          : {}),
      },
      opponent: {
        ...snapshot.opponent,
        handCardNumbers: [...frame.opponentCardNumbers],
        handCount: frame.opponentCardNumbers.length,
        ...(opponentLife
          ? {
              lifeNumbers: [...opponentLife],
              lifeCount: opponentLife.length,
              lifeFaceUp: opponentLife.map((number) => ({ faceUp: true, number })),
            }
          : {}),
      },
    };
  });
}
