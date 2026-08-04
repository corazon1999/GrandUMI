/**
 * GameProtocol.ts — 游戏协议接收层
 *
 * 服务器是权威结算端，客户端只是镜像。所有游戏状态变化都通过 MsgGameState 推送。
 * 这里只负责把 MsgGameState 灌进 gameStore，把通知类消息抛给 eventBus。
 */

import { eventBus } from "./eventBus";
import type {
  MsgBase,
  MsgGameState,
  MsgPlayerDisconnected,
  MsgPlayerReconnected,
  MsgDuelOver,
  MsgActionRejected,
  MsgGameChat,
} from "@/types/net";
import { useGameStore } from "@/store/gameStore";
import { useNetStore } from "@/store/netStore";
import { matchRecorder } from "@/data/matchRecorder";

let registered = false;

export function registerGameProtocols() {
  if (registered) return;
  registered = true;

  eventBus.on("message", (msg: MsgBase) => {
    switch (msg.proto) {
      case "MsgGameState": {
        const gs = msg as MsgGameState;
        useGameStore.getState().syncFromServer(gs);
        // 本地录制对局快照流（仅玩家视角；观战不记）→ 供首页战绩/回放
        matchRecorder.onSnapshot(gs);
        // 重启恢复/刷新后：若收到己方对局快照（非观战、未结束）但当前不在 /game，
        // 说明该账号有进行中的对局，自动回到对战页（服务端 TryReclaim 已发回 Resync 快照）。
        if (
          gs.viewerKind === "player" &&
          !gs.isGameOver &&
          typeof window !== "undefined" &&
          window.location.pathname !== "/game"
        ) {
          useGameStore.getState().setMode("Player");
          useNetStore.getState().setNavigateTo("/game");
        }
        break;
      }

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

      case "MsgGameChat": {
        const m = msg as MsgGameChat;
        eventBus.emit("gameChat", {
          text: m.text ?? "",
          code: m.code ?? null,
          fromSeat: m.fromSeat ?? -1,
          fromAccount: m.fromAccount,
          fromName: m.fromName ?? "玩家",
          fromRole: m.fromRole ?? "spectator",
        });
        break;
      }
    }
  });

  // 对局中的网络重连会由 HomeProtocol 自动重新登录；整页刷新则由玩家
  // 在登录页确认账号后触发登录。后端 OnLogin → TryReclaim 会按账号找回房间
  // 并回发完整 Resync 快照。
  // 不再在此直接 requestState：整页刷新后是全新 SessionId，尚未绑定房间，
  // 直接 requestState 会落空甚至被判“对局已结束”而误踢。
}
