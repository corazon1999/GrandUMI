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

  /** 使用反击：手牌事件 / 场上反击图标 */
  playCounterEvent: (handIndex: number) => send("PlayCounter", { handIndex }),
  playCounterIcon:  (fieldCardId: string) => send("PlayCounter", { fieldCardId, useCounterIcon: true }),
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
};
