/**
 * GameRequest.ts — 游戏动作发送层
 *
 * 所有动作均通过 MsgGameAction 发到服务器；服务器结算后通过 MsgGameState 推回。
 * 客户端不在本地预演任何状态变更。
 */

import { NetManager } from "./NetManager";
import type { MsgGameAction, MsgPromptResponse, MsgRequestState, GameActionType } from "@/types/net";
import { useGameStore } from "@/store/gameStore";

function send(action: GameActionType, data: Record<string, unknown> = {}) {
  useGameStore.getState().setPending(true);
  NetManager.send({
    proto: "MsgGameAction",
    action,
    data,
  } as MsgGameAction);
}

export const GameRequest = {
  /** 重抽决策（双方初始 5 张后） */
  mulligan: (redraw: boolean) => send("Mulligan", { redraw }),

  /** 出牌：handIndex = 当前手牌列表下标 */
  playCard: (handIndex: number) => send("PlayCard", { handIndex }),

  /** 赋予咚：将一张活跃咚附给领袖或场上角色 */
  attachDon: (targetId: string | "leader", count = 1) =>
    send("AttachDon", { targetId, count }),

  /** 攻击宣言 */
  attack: (attackerId: string, target: { isLeader: true } | { isLeader: false; cardId: string }) =>
    send("Attack",
      target.isLeader
        ? { attackerId, targetIsLeader: true }
        : { attackerId, targetIsLeader: false, targetId: target.cardId }),

  /** 宣言【阻挡者】 */
  declareBlocker: (blockerId: string) => send("DeclareBlocker", { blockerId }),
  passBlock:      () => send("PassBlock"),

  /** 使用反击值：弃一张带反击值的手牌，为被攻击目标加力量 */
  playCounterFromHand: (handIndex: number) => send("PlayCounter", { handIndex, useCounterIcon: true }),
  /** 反击事件：从手牌打出带 EventCounter 的事件 */
  playCounterEvent: (handIndex: number) => send("PlayCounter", { handIndex }),
  passCounter:      () => send("PassCounter"),

  /** 使用启动效果 */
  useEffect: (sourceId: string, effectKey: string, extra: Record<string, unknown> = {}) =>
    send("UseEffect", { sourceId, effectKey, ...extra }),

  endTurn:       () => send("EndTurn"),
  confirmDamage: () => send("ConfirmDamage"),
  surrender:     () => send("Surrender"),

  /** 响应服务器发起的 Prompt */
  respondPrompt: (promptId: string, chosen: string[]) => {
    useGameStore.getState().setPending(true);
    NetManager.send({ proto: "MsgPromptResponse", promptId, chosen } as MsgPromptResponse);
  },

  /** 断线重连后请求完整快照 */
  requestState: () => {
    NetManager.send({ proto: "MsgRequestState" } as MsgRequestState);
  },

  /** 对手断线宽限期内，主动请求即时结束对局 */
  endByDisconnect: () => {
    NetManager.send({ proto: "MsgEndByDisconnect" } as import("@/types/net").MsgBase);
  },

  debugAddCard:    (cardNumber: string) => send("DebugAddCard", { cardNumber }),
  debugAddDon:     (count = 1) => send("DebugAddDon", { count }),
  debugRefreshDon: () => send("DebugRefreshDon"),
  debugSummon:     (cardNumber: string, target: "self" | "opponent" = "self") => send("DebugSummon", { cardNumber, target }),
  debugKoAll:      (target: "self" | "opponent" = "self") => send("DebugKoAll", { target }),
  debugRestAll:    (target: "self" | "opponent" = "self") => send("DebugRestAll", { target }),
  debugLeaderAttack: () => send("DebugLeaderAttack"),

  /** 局内聊天：发送一条消息到本对局房间（双方+观战者） */
  sendGameChat: (text: string, code?: string) => {
    const t = text.trim();
    if (!t) return;
    NetManager.send({ proto: "MsgGameChat", Text: t.slice(0, 100), Code: code ?? null } as unknown as MsgRequestState);
  },
};
