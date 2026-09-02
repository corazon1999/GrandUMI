/** 开场气泡在牌桌上的可见时长。 */
export const OPENING_BUBBLE_MS = 4_200;
/** 战败领袖闪白、裂纹、碎片和冲击波的完整时长。 */
export const DEFEAT_IMPACT_MS = 1_080;
/** 胜利气泡进入并达到完全可见所需时间。 */
export const VICTORY_ENTRANCE_MS = 320;
/** 气泡完全可见后，结算面板前必须保留的时间。 */
export const VICTORY_HOLD_MS = 2_000;

/**
 * 在线对局中，同一场比赛的较旧快照不得回滚已经展示的权威状态。
 * 回放允许主动倒退；不同 matchId 则代表新比赛，必须正常接收。
 */
export function shouldIgnoreLiveGameStateSnapshot({
  mode,
  previousTick,
  incomingTick,
  previousMatchId,
  incomingMatchId,
}) {
  return mode !== "Playback"
    && typeof previousMatchId === "string"
    && previousMatchId.length > 0
    && previousMatchId === incomingMatchId
    && incomingTick < previousTick;
}

/**
 * 终局是同一场在线比赛的吸收态。重复、同 Tick 或缺少演出字段的迟到快照，
 * 均不能把结算状态恢复成进行中；回放和明确的新 matchId 不受此限制。
 */
export function shouldPreserveTerminalGameState({
  mode,
  previousIsGameOver,
  incomingIsGameOver,
  previousTick,
  incomingTick,
  previousMatchId,
  incomingMatchId,
}) {
  if (mode === "Playback" || !previousIsGameOver || incomingIsGameOver) return false;
  if (previousMatchId && incomingMatchId) return previousMatchId === incomingMatchId;
  return incomingTick <= previousTick;
}

export function createInitialGameCinematicState() {
  return {
    matchId: null,
    seenOpeningEventIds: [],
    openingBubbles: [],
    terminalEventId: null,
    terminal: null,
    phase: "idle",
    phaseStartedAt: 0,
    settlementReady: false,
  };
}

function copyState(state) {
  return {
    ...state,
    seenOpeningEventIds: [...state.seenOpeningEventIds],
    openingBubbles: state.openingBubbles.map((bubble) => ({ ...bubble })),
    terminal: state.terminal
      ? {
          ...state.terminal,
          victory: state.terminal.victory ? { ...state.terminal.victory } : null,
        }
      : null,
  };
}

function resetForMatch(state, matchId) {
  if (!matchId || state.matchId === matchId) return copyState(state);
  return { ...createInitialGameCinematicState(), matchId };
}

/**
 * 合并服务端权威演出元数据。eventId 是唯一去重依据，重复快照不得重启动画或延长计时。
 */
export function ingestGameCinematic(state, snapshot, now = Date.now()) {
  if (!snapshot || typeof snapshot.matchId !== "string" || !snapshot.matchId)
    return copyState(state);

  const next = resetForMatch(state, snapshot.matchId);
  for (const event of Array.isArray(snapshot.openingEvents) ? snapshot.openingEvents : []) {
    if (!event || typeof event.eventId !== "string" || !event.eventId) continue;
    if (event.displaySide !== "self" && event.displaySide !== "opponent") continue;
    if (next.seenOpeningEventIds.includes(event.eventId)) continue;
    next.seenOpeningEventIds.push(event.eventId);
    next.openingBubbles.push({ ...event, expiresAt: now + OPENING_BUBBLE_MS });
  }

  const terminal = snapshot.terminal;
  if (!terminal || typeof terminal.eventId !== "string" || !terminal.eventId)
    return next;
  if (next.terminalEventId === terminal.eventId) return next;

  next.terminalEventId = terminal.eventId;
  next.terminal = {
    ...terminal,
    victory: terminal.victory ? { ...terminal.victory } : null,
  };
  next.openingBubbles = [];
  next.phaseStartedAt = now;
  next.settlementReady = false;
  if (terminal.loserSide === "self" || terminal.loserSide === "opponent") {
    next.phase = "impact";
  } else if (terminal.victory) {
    next.phase = "victory";
  } else {
    next.phase = "complete";
    next.settlementReady = true;
  }
  return next;
}

/** 旧服务端、错误终止或无房间恢复回包：不播放猜测动画，立即释放结算遮罩。 */
export function finishGameCinematicFallback(state, now = Date.now()) {
  if (state.settlementReady && state.phase === "complete") return copyState(state);
  return {
    ...copyState(state),
    openingBubbles: [],
    terminalEventId: state.terminalEventId ?? "legacy-terminal",
    terminal: state.terminal,
    phase: "complete",
    phaseStartedAt: now,
    settlementReady: true,
  };
}

/** 只有从未接收过权威终局事件时才走旧协议兜底，迟到快照不能截断正在播放的终局演出。 */
export function needsGameCinematicFallback(state, isGameOver) {
  return isGameOver && !state.terminalEventId;
}

/** 清理到期的开场气泡；终局阶段永远不保留开场气泡。 */
export function pruneOpeningBubbles(state, now = Date.now()) {
  const next = copyState(state);
  next.openingBubbles = next.phase === "idle"
    ? next.openingBubbles.filter((bubble) => bubble.expiresAt > now)
    : [];
  return next;
}

/**
 * 依据绝对开始时间推进阶段。后台标签页、组件重挂载或迟到定时器会一次追赶到正确状态，
 * 而不是从头再等一遍。reducedMotion 只压缩运动阶段，不省略胜利宣言完整可见后的 2 秒。
 */
export function advanceGameCinematic(state, now = Date.now(), reducedMotion = false) {
  let next = pruneOpeningBubbles(state, now);
  for (let guard = 0; guard < 3; guard++) {
    if (next.phase === "impact") {
      const deadline = next.phaseStartedAt + (reducedMotion ? 0 : DEFEAT_IMPACT_MS);
      if (now < deadline) break;
      if (next.terminal?.victory) {
        next.phase = "victory";
        next.phaseStartedAt = deadline;
      } else {
        next.phase = "complete";
        next.phaseStartedAt = deadline;
        next.settlementReady = true;
      }
      continue;
    }
    if (next.phase === "victory") {
      const entrance = reducedMotion ? 0 : VICTORY_ENTRANCE_MS;
      const deadline = next.phaseStartedAt + entrance + VICTORY_HOLD_MS;
      if (now < deadline) break;
      next.phase = "complete";
      next.phaseStartedAt = deadline;
      next.settlementReady = true;
      continue;
    }
    break;
  }
  return next;
}

/** 返回下一次状态推进的绝对时间；null 表示当前无需定时器。 */
export function nextGameCinematicDeadline(state, reducedMotion = false) {
  const bubbleDeadline = state.phase === "idle" && state.openingBubbles.length > 0
    ? Math.min(...state.openingBubbles.map((bubble) => bubble.expiresAt))
    : Number.POSITIVE_INFINITY;
  let phaseDeadline = Number.POSITIVE_INFINITY;
  if (state.phase === "impact") {
    phaseDeadline = state.phaseStartedAt + (reducedMotion ? 0 : DEFEAT_IMPACT_MS);
  } else if (state.phase === "victory") {
    phaseDeadline = state.phaseStartedAt
      + (reducedMotion ? 0 : VICTORY_ENTRANCE_MS)
      + VICTORY_HOLD_MS;
  }
  const deadline = Math.min(bubbleDeadline, phaseDeadline);
  return Number.isFinite(deadline) ? deadline : null;
}
