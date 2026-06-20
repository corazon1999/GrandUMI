/**
 * GameRequest.ts — 游戏动作发送层
 *
 * 所有动作均通过 MsgGameAction 发到服务器；服务器结算后通过 MsgGameState 推回。
 * 客户端不在本地预演任何状态变更。
 */

import { NetManager } from "./NetManager";
import type { MsgBase, MsgGameAction, MsgPromptResponse, MsgRequestState, GameActionType } from "@/types/net";
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

  /** GM 调试：按编号加一张牌到自己手牌 */
  debugAddCard: (cardNumber: string) => send("DebugAddCard", { cardNumber }),
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

  /** 响应服务器发起的 Prompt */
  respondPrompt: (promptId: string, chosen: string[]) => {
    useGameStore.getState().setPending(true);
    NetManager.send({ proto: "MsgPromptResponse", promptId, chosen } as MsgPromptResponse);
  },

  /** 断线重连后请求完整快照 */
  requestState: () => {
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
};
