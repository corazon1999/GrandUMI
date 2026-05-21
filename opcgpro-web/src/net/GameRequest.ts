/**
 * GameRequest.ts — 游戏动作发送层
 * 对应 C# RequestManager.cs，所有游戏操作经此模块发送到服务器
 *
 * 传输机制：
 *   暂通过 MsgTransmit 透传（兼容现有服务器），格式为 "^" 分隔字符串
 *   操作名与 C# RequestManager 完全一致
 */

import { NetManager } from "./NetManager";
import type { MsgTransmit } from "@/types/net";
import { useGameStore } from "@/store/gameStore";

/** 构造并发送 MsgTransmit，同时本地执行结算 */
function send(actionParts: string[]) {
  const msg = actionParts.join("^");

  NetManager.send({
    proto: "MsgTransmit",
    Msg: msg,
  } as MsgTransmit);

  // 本地客户端同步执行（对应 C# SendRequest 中的 AcceptManager.GetRequest）
  useGameStore.getState().executeAction(msg, false);
}

export const GameRequest = {
  /** 出牌：从手牌登场角色/舞台到场上 */
  playCard: (handIndex: number) => {
    const cards = useGameStore.getState().my.hand.cards;
    const card = cards[handIndex];
    if (!card) return;
    send(["AppearFromHand", card.number, String(handIndex), "False", "活跃", "True"]);
  },

  /** 回合开始时抽牌：仅当非第一轮时抽 */
  drawForTurn: () => {
    const store = useGameStore.getState();
    if (store.myDeckCards.length === 0) return;
    const cardNumber = store.myDeckCards[0];
    send(["Draw", cardNumber]);
  },

  /** 附加咚到场上角色（字段: AddDong^fieldIndex^count^isActive） */
  attachDon: (fieldIndex: number, count: number) => {
    send(["AddDong", String(fieldIndex), String(count), "True"]);
  },

  /** 攻击：选择己方攻击者 + 目标（对方角色索引或 'leader'） */
  attack: (attackerIndex: number, target: number | "leader") =>
    send(["Attack", String(attackerIndex), String(target)]),

  /** 防御：选择己方防御者 */
  block: (blockerIndex: number) =>
    send(["Block", String(blockerIndex)]),

  /** 反击：使用手牌中带 Counter 值的牌 */
  counter: (handIndex: number) =>
    send(["Counter", String(handIndex)]),

  /** 使用卡牌效果 */
  useEffect: (cardIndex: number, targetIndex?: number) =>
    send(["UseEffect", String(cardIndex), String(targetIndex ?? -1)]),

  /** 结束当前回合 */
  endTurn: () => {
    NetManager.send({ proto: "MsgTransmit", Msg: "EndTurn" } as MsgTransmit);
    useGameStore.getState().nextTurn();
  },

  /** 确认伤害 */
  confirmDamage: () =>
    send(["ConfirmDamage"]),

  /** 投降 */
  surrender: () => {
    NetManager.send({ proto: "MsgSurrender" } as { proto: string });
  },
};
