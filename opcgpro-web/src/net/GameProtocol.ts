/**
 * GameProtocol.ts — 游戏协议接收层
 *
 * 服务器是权威结算端，客户端只是镜像。所有游戏状态变化都通过 MsgGameState 推送。
 * 这里只负责把 MsgGameState 灌进 gameStore，把通知类消息抛给 eventBus。
 */

import { eventBus } from "./eventBus";
import { GameRequest } from "./GameRequest";
import type {
  MsgBase,
  MsgGameState,
  MsgPlayerDisconnected,
  MsgPlayerReconnected,
  MsgDuelOver,
  MsgActionRejected,
} from "@/types/net";
import { useGameStore } from "@/store/gameStore";
import { useNetStore } from "@/store/netStore";

let registered = false;

export function registerGameProtocols() {
  if (registered) return;
  registered = true;

  eventBus.on("message", (msg: MsgBase) => {
    switch (msg.proto) {
      case "MsgGameState":
        useGameStore.getState().syncFromServer(msg as MsgGameState);
        break;

      case "MsgActionRejected":
        useGameStore.getState().setPending(false);
        eventBus.emit("actionRejected", { reason: (msg as MsgActionRejected).reason });
        console.warn("[GameProtocol] action rejected:", (msg as MsgActionRejected).reason);
        break;

      case "MsgPlayerDisconnected":
        eventBus.emit("opponentDisconnected", {
          gracePeriodSeconds: (msg as MsgPlayerDisconnected).gracePeriodSeconds,
        });
        break;

      case "MsgPlayerReconnected":
        eventBus.emit("opponentReconnected");
        break;

      case "MsgDuelOver":
        useGameStore.getState().setPending(false);
        useNetStore.getState().setMatchState("idle");
        useNetStore.getState().setOpponentName("");
        eventBus.emit("duelOver", {
          isWin: (msg as MsgDuelOver).IsWin,
          description: (msg as MsgDuelOver).Description,
        });
        break;
    }
  });

  // 重连后自动请求完整快照
  eventBus.on("reconnected", () => {
    GameRequest.requestState();
  });
}
