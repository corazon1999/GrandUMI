import type { ConnectionState } from "@/net/eventBus";

export type InactivityWarningVisibility = {
  active: "my" | "opponent" | null;
  warning: boolean;
  isGameOver: boolean;
  connState: ConnectionState;
  connectionEpoch: number;
  snapshotConnectionEpoch: number;
};

/**
 * 每次从可交互连接离开时推进世代；同一轮重连中的中间状态不会重复推进。
 * 对局快照记录接收时的连接世代，用来拒绝断线前遗留的旧交互状态。
 */
export function nextConnectionEpoch(
  currentEpoch: number,
  previousState: ConnectionState,
  nextState: ConnectionState,
): number {
  return previousState === "connected" && nextState !== "connected"
    ? currentEpoch + 1
    : currentEpoch;
}

/** 只有当前连接已经收到新权威快照时，挂机确认才允许重新成为交互层。 */
export function shouldShowInactivityWarning({
  active,
  warning,
  isGameOver,
  connState,
  connectionEpoch,
  snapshotConnectionEpoch,
}: InactivityWarningVisibility): boolean {
  return active === "my"
    && warning
    && !isGameOver
    && connState === "connected"
    && snapshotConnectionEpoch === connectionEpoch;
}
