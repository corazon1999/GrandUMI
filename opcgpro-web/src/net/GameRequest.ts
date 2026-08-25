/**
 * GameRequest.ts — 游戏动作发送层
 *
 * 所有动作均通过 MsgGameAction 发到服务器；服务器结算后通过 MsgGameState 推回。
 * 对贴咚、攻击横置和出牌仅做可回滚的即时视觉反馈；规则结算仍完全以服务端为准。
 * 贴咚一经确认就立即发往服务端，不在客户端保留可撤回的未提交队列。
 */

import { NetManager } from "./NetManager";
import type { MsgBase, MsgFriendChat, MsgGameAction, MsgLeaveGameChat, MsgPromptResponse, MsgRequestState, GameActionType } from "@/types/net";
import { useGameStore } from "@/store/gameStore";
import { useSettingsStore } from "@/store/settingsStore";

type PendingLatency = { requestId: string; action: string; startedAt: number };
const pendingLatencies = new Map<string, PendingLatency>();
let requestSequence = 0;

export type PendingAttachDonConfirmation = {
  targetId: string | "leader";
  count: number;
};

let pendingAttachDonConfirmation: PendingAttachDonConfirmation | null = null;
const pendingAttachDonConfirmationListeners = new Set<() => void>();

function notifyPendingAttachDonConfirmation() {
  pendingAttachDonConfirmationListeners.forEach((listener) => listener());
}

export function getPendingAttachDonConfirmation(): PendingAttachDonConfirmation | null {
  return pendingAttachDonConfirmation;
}

export function subscribePendingAttachDonConfirmation(listener: () => void) {
  pendingAttachDonConfirmationListeners.add(listener);
  return () => pendingAttachDonConfirmationListeners.delete(listener);
}

function createRequestId(): string {
  return `${Date.now().toString(36)}-${(++requestSequence).toString(36)}`;
}

function markActionSent(requestId: string, action: string) {
  if (typeof performance === "undefined") return;
  const startedAt = performance.now();
  pendingLatencies.set(requestId, { requestId, action, startedAt });
  for (const [id, pending] of pendingLatencies) {
    if (startedAt - pending.startedAt > 60_000) pendingLatencies.delete(id);
  }
}

/**
 * 棋钟控制消息不改变卡牌状态，也不占用全屏 pending 遮罩。
 * requestId 仍交给服务端去重，双击加时或网络重发都不会重复增加时间。
 */
function sendClockControl(
  action: "PlayerActivity" | "RequestTurnExtension",
  data: Record<string, unknown> = {},
) {
  return NetManager.send({
    proto: "MsgGameAction",
    action,
    data,
    requestId: createRequestId(),
  } as MsgGameAction);
}

/** 收到对应权威快照时结束本次客户端→服务端→客户端计时。 */
export function completePendingActionLatency(requestId: string | null | undefined, tick: number) {
  completeLatency(requestId, `到快照 Tick=${tick}`);
}

/** 动作被拒绝时也完成对应计时，避免污染下一次正常动作。 */
export function completeRejectedActionLatency(requestId: string | null | undefined) {
  completeLatency(requestId, "到拒绝回包");
}

function completeLatency(requestId: string | null | undefined, suffix: string) {
  if (typeof performance === "undefined") return;
  const pending = requestId
    ? pendingLatencies.get(requestId)
    : pendingLatencies.values().next().value as PendingLatency | undefined;
  if (!pending) return;

  pendingLatencies.delete(pending.requestId);
  const elapsed = performance.now() - pending.startedAt;
  NetManager.recordActionLatency(elapsed);
  if (elapsed >= 80) {
    console.info(`[延迟] ${pending.action} ${suffix} ${elapsed.toFixed(1)}ms`);
  }
}

function sendNow(
  action: GameActionType,
  data: Record<string, unknown> = {},
  optimistic?: () => void,
) {
  const store = useGameStore.getState();
  store.setPending(true);
  optimistic?.();
  const requestId = createRequestId();
  const sent = NetManager.send({
    proto: "MsgGameAction",
    action,
    data,
    requestId,
  } as MsgGameAction);
  if (sent) markActionSent(requestId, action);
  else {
    pendingLatencies.delete(requestId);
    useGameStore.getState().rollbackOptimistic();
    useGameStore.getState().setPending(false);
  }
  return sent;
}

function send(
  action: GameActionType,
  data: Record<string, unknown> = {},
  optimistic?: () => void,
) {
  return sendNow(action, data, optimistic);
}

function submitAttachDon(targetId: string | "leader", count: number) {
  const safeCount = Math.max(1, Math.floor(count));
  return sendNow(
    "AttachDon",
    { targetId, count: safeCount },
    () => useGameStore.getState().optimisticAttachDon(targetId, safeCount),
  );
}

function requestAttachDonConfirmation(targetId: string | "leader", count: number) {
  const safeCount = Math.max(1, Math.floor(count));
  pendingAttachDonConfirmation = {
    targetId,
    count: safeCount,
  };
  notifyPendingAttachDonConfirmation();
  return true;
}

export function confirmPendingAttachDon() {
  const pending = pendingAttachDonConfirmation;
  if (!pending) return false;
  pendingAttachDonConfirmation = null;
  notifyPendingAttachDonConfirmation();
  return submitAttachDon(pending.targetId, pending.count);
}

export function cancelPendingAttachDonConfirmation() {
  if (!pendingAttachDonConfirmation) return false;
  pendingAttachDonConfirmation = null;
  notifyPendingAttachDonConfirmation();
  return true;
}

/** 离开对局或重连时关闭尚未确认、因而尚未发送的贴咚弹窗。 */
export function clearPendingAttachDonState() {
  const hadConfirmation = pendingAttachDonConfirmation !== null;
  pendingAttachDonConfirmation = null;
  if (hadConfirmation) notifyPendingAttachDonConfirmation();
  return hadConfirmation;
}

export const GameRequest = {
  /** 开局骰点胜者选择自己先手或后手。 */
  chooseFirstPlayer: (goFirst: boolean) => send("ChooseFirstPlayer", { goFirst }),

  /** 重抽决策（双方初始 5 张后） */
  mulligan: (redraw: boolean) => send("Mulligan", { redraw }),

  /** 出牌：handIndex = 当前手牌列表下标 */
  playCard: (handIndex: number, overflowTrashCardId?: string) =>
    send("PlayCard", {
      handIndex,
      ...(overflowTrashCardId ? { overflowTrashCardId } : {}),
    }, () => useGameStore.getState().optimisticPlayCard(handIndex)),

  /** 请求赋予咚；可先本地确认，但确认后立即提交且不可撤回。 */
  attachDon: (targetId: string | "leader", count = 1) => {
    const safeCount = Math.max(1, Math.floor(count));
    return useSettingsStore.getState().confirmAttachDon
      ? requestAttachDonConfirmation(targetId, safeCount)
      : submitAttachDon(targetId, safeCount);
  },

  /** 离开对局时关闭尚未发送的确认弹窗。 */
  cancelPendingAttachDon: clearPendingAttachDonState,

  /** 攻击宣言 */
  attack: (attackerId: string, target: { isLeader: true } | { isLeader: false; cardId: string }) =>
    send("Attack",
      target.isLeader
        ? { attackerId, targetIsLeader: true }
        : { attackerId, targetIsLeader: false, targetId: target.cardId },
      () => useGameStore.getState().optimisticAttack(attackerId)),

  /** 宣言【阻挡者】 */
  declareBlocker: (blockerId: string) => send("DeclareBlocker", { blockerId }),
  passBlock:      () => send("PassBlock"),

  /** 使用反击：从手牌弃一张带反击值的牌，为被攻击目标加力量（服务端读 handIndex + useCounterIcon） */
  playCounterFromHand: (handIndex: number) => send("PlayCounter", { handIndex, useCounterIcon: true }),
  /** 反击事件（【反击】效果）：从手牌打出带 EventCounter 的事件，服务端扣费+结算 EventCounter */
  playCounterEvent: (handIndex: number) => send("PlayCounter", { handIndex }),
  passCounter:      () => send("PassCounter"),

  /** 使用启动效果 */
  useEffect: (sourceId: string, effectKey: string, extra: Record<string, unknown> = {}) =>
    send("UseEffect", { sourceId, effectKey, ...extra }),

  endTurn:       () => send("EndTurn"),
  confirmDamage: () => send("ConfirmDamage"),
  surrender:     () => send("Surrender"),
  requestDraw:   (description: string) => send("RequestDraw", { description }),
  respondDraw:   (accept: boolean) => send("RespondDraw", { accept }),

  /** 每位玩家每局一次，将当前回合剩余时间增加 2 分钟（最高不超过 8 分钟）。 */
  requestTurnExtension: () => sendClockControl("RequestTurnExtension"),

  /** 挂机提醒弹窗中的明确在线确认；服务端据此把本段连续无操作计时归零。 */
  confirmInactivityPresence: () => sendClockControl("PlayerActivity", { kind: "presence" }),

  /** GM 调试：按编号加一张牌到自己手牌 */
  debugAddCard: (cardNumber: string) => send("DebugAddCard", { cardNumber }),
  /** GM 调试：按编号将一张牌置于指定一方生命区顶端 */
  debugAddLife: (cardNumber: string, target: "self" | "opponent" = "self") =>
    send("DebugAddLife", { cardNumber, target }),
  /** GM 调试：加 count 张活跃咚 */
  debugAddDon: (count = 1) => send("DebugAddDon", { count }),
  /** GM 调试：刷新所有咚（回费用区并竖直/活跃，含解除赋予） */
  debugRefreshDon: () => send("DebugRefreshDon"),
  /** GM 调试：按编号直接召唤到场上（target 默认自己场上） */
  debugSummon: (cardNumber: string, target: "self" | "opponent" = "self") =>
    send("DebugSummon", { cardNumber, target }),
  /** GM 调试：KO 指定一方场上全部角色（触发【K.O.时】等效果，target 默认自己场上） */
  debugKoAll: (target: "self" | "opponent" = "self") =>
    send("DebugKoAll", { target }),
  /** GM 调试：横置指定一方场上全部角色（纯状态变更，target 默认自己场上） */
  debugRestAll: (target: "self" | "opponent" = "self") =>
    send("DebugRestAll", { target }),
  /** GM 调试：对手领袖向我方领袖发起一次完整攻击（含阻挡/反击/伤害结算） */
  debugLeaderAttack: () => send("DebugLeaderAttack"),
  /** GM 调试：按当前领航颜色运行 OP17 全卡独立场景巡检 */
  debugRunOP17Coverage: () => send("DebugRunOP17Coverage"),

  /** 响应服务器发起的 Prompt */
  respondPrompt: (promptId: string, chosen: string[]) => {
    useGameStore.getState().setPending(true);
    const requestId = createRequestId();
    const sent = NetManager.send({ proto: "MsgPromptResponse", promptId, chosen, requestId } as MsgPromptResponse);
    if (sent) markActionSent(requestId, "PromptResponse");
    else {
      pendingLatencies.delete(requestId);
      useGameStore.getState().rollbackOptimistic();
      useGameStore.getState().setPending(false);
    }
    return sent;
  },

  /** 断线重连后请求完整快照 */
  requestState: () => {
    clearPendingAttachDonState();
    useGameStore.getState().rollbackOptimistic();
    useGameStore.getState().setPending(false);
    NetManager.send({ proto: "MsgRequestState" } as MsgRequestState);
  },

  /** 对手断线宽限期内，主动请求即时结束对局（判对手负），不再干等后端计时器 */
  endByDisconnect: () => {
    NetManager.send({ proto: "MsgEndByDisconnect" } as MsgBase);
  },

  /** 局内聊天：发送一条消息到本对局房间（双方+观战者）。code=预设短语编号（自由文字省略）。 */
  sendGameChat: (text: string, code?: string) => {
    const t = text.trim();
    if (!t) return;
    NetManager.send({ proto: "MsgGameChat", Text: t.slice(0, 100), Code: code ?? null } as unknown as MsgBase);
  },

  /** 好友实时私聊：服务端会校验双方仍为好友且接收方在线。 */
  sendFriendChat: (toAccount: string, text: string) => {
    const t = text.trim();
    if (!toAccount || !t) return;
    NetManager.send({ proto: "MsgFriendChat", toAccount, text: t.slice(0, 100) } as MsgFriendChat);
  },

  /** 返回大厅时退出上一场对局的赛后聊天组。 */
  leaveGameChat: () => {
    NetManager.send({ proto: "MsgLeaveGameChat" } as MsgLeaveGameChat);
  },

  requestSpectatorHand: () => NetManager.send({ proto: "MsgRequestSpectatorHand" } as MsgBase),

  respondSpectatorHand: (requestId: string, accept: boolean) => {
    NetManager.send({ proto: "MsgRespondSpectatorHand", requestId, accept } as MsgBase);
  },

  kickSpectator: (spectatorAccount: string) => {
    NetManager.send({ proto: "MsgKickSpectator", spectatorAccount } as MsgBase);
  },
};
