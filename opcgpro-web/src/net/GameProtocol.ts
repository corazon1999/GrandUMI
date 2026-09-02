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
  MsgGameRecoveryPaused,
  MsgGameChat,
  MsgFriendChat,
  MsgSpectatorList,
  MsgSpectatorHandRequest,
  MsgSpectatorHandStatus,
  MsgSpectatorHandResponse,
  MsgKickSpectator,
  MsgSpectatorKicked,
  MsgRulesetUpdated,
} from "@/types/net";
import { useGameStore } from "@/store/gameStore";
import { useNetStore } from "@/store/netStore";
import { matchRecorder } from "@/data/matchRecorder";
import {
  completePendingActionLatency,
  completeRejectedActionLatency,
} from "./GameRequest";
import { showMessage } from "@/components/ui/MessageBox";
import type { ChatDecorationSlot } from "@/store/netStore";

type DecoratedGameChatWire = MsgGameChat & {
  displaySide?: "self" | "opponent" | null;
  sentAt?: number;
  decoration?: {
    id: string;
    slot: ChatDecorationSlot;
    rarity: "common" | "rare" | "epic" | "legendary";
    styleToken: string;
  } | null;
};

type ChatDecorationSendResultWire = MsgBase & {
  proto: "MsgChatDecorationSend";
  result?: boolean;
  logStr?: string | null;
};

let registered = false;

export function registerGameProtocols() {
  if (registered) return;
  registered = true;

  eventBus.on("message", (msg: MsgBase) => {
    switch (msg.proto) {
      case "MsgGameState": {
        const gs = msg as MsgGameState;
        completePendingActionLatency(gs.requestId, gs.tick ?? 0);
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
        completeRejectedActionLatency((msg as MsgActionRejected).requestId);
        useGameStore.getState().rollbackOptimistic();
        useGameStore.getState().setPending(false);
        eventBus.emit("actionRejected", { reason: (msg as MsgActionRejected).reason });
        console.warn("[GameProtocol] action rejected:", (msg as MsgActionRejected).reason);
        break;

      case "MsgGameRecoveryPaused": {
        const paused = msg as MsgGameRecoveryPaused;
        useGameStore.getState().rollbackOptimistic();
        useGameStore.getState().setPending(false);
        showMessage(
          paused.message || "恢复存储暂时不可用，对局已安全暂停，请稍后重新连接。",
          "error",
        );
        console.error("[GameProtocol] recovery paused:", paused.roomId, paused.reason);
        break;
      }

      case "MsgPlayerDisconnected":
        eventBus.emit("opponentDisconnected", {
          gracePeriodSeconds: (msg as MsgPlayerDisconnected).gracePeriodSeconds,
        });
        break;

      case "MsgPlayerReconnected":
        eventBus.emit("opponentReconnected");
        break;

      case "MsgRulesetUpdated": {
        const update = msg as MsgRulesetUpdated;
        const changedCards = update.changedCards?.length
          ? `（涉及 ${update.changedCards.join("、")}）`
          : "";
        showMessage(
          `${update.logStr ?? "卡牌效果已更新，将从下一局开始生效"}${changedCards}`,
          "info",
        );
        break;
      }

      case "MsgDuelOver":
        useGameStore.getState().setPending(false);
        useNetStore.getState().setMatchState("idle");
        useNetStore.getState().setOpponentName("");
        eventBus.emit("duelOver", {
          isWin: (msg as MsgDuelOver).IsWin,
          description: (msg as MsgDuelOver).Description,
        });
        break;

      case "MsgChatDecorationSend": {
        const response = msg as ChatDecorationSendResultWire;
        if (response.result === false) {
          showMessage(response.logStr?.trim() || "聊天装饰发送失败，请稍后重试。", "error");
        }
        break;
      }

      case "MsgGameChat": {
        const m = msg as DecoratedGameChatWire;
        eventBus.emit("gameChat", {
          text: m.text ?? "",
          code: m.code ?? null,
          fromSeat: m.fromSeat ?? -1,
          fromAccount: m.fromAccount,
          fromName: m.fromName ?? "玩家",
          fromRole: m.fromRole ?? "spectator",
          displaySide: m.displaySide ?? null,
          sentAt: m.sentAt,
          decoration: m.decoration ?? null,
        });
        break;
      }

      case "MsgFriendChat": {
        const m = msg as MsgFriendChat;
        if (m.result === false) {
          showMessage(m.logStr ?? "好友消息发送失败", "error");
          break;
        }
        if (!m.id || !m.text || !m.fromAccount || !m.toAccount) break;
        useNetStore.getState().addFriendChatMessage({
          id: m.id,
          text: m.text,
          fromAccount: m.fromAccount,
          fromName: m.fromName ?? m.fromAccount,
          toAccount: m.toAccount,
          toName: m.toName ?? m.toAccount,
          sentAt: m.sentAt ?? Date.now(),
        });
        break;
      }

      case "MsgSpectatorList": {
        const spectatorList = msg as MsgSpectatorList;
        useGameStore.getState().setSpectatorNames(spectatorList.spectators ?? []);
        useGameStore.getState().setSpectatorDetails(spectatorList.details ?? []);
        break;
      }

      case "MsgSpectatorHandRequest": {
        const request = msg as MsgSpectatorHandRequest;
        useGameStore.getState().addSpectatorHandRequest(request);
        showMessage(`${request.spectatorName} 申请查看你的手牌`, "info");
        break;
      }

      case "MsgSpectatorHandStatus": {
        const status = msg as MsgSpectatorHandStatus;
        if (status.status === "pending") {
          useGameStore.getState().setObserverHandRequestStatus("pending");
        } else if (status.status === "approved") {
          useGameStore.getState().setObserverHandRequestStatus("idle");
        } else {
          useGameStore.getState().setObserverHandRequestStatus(
            status.retryAfterMs ? "cooldown" : "idle",
            status.retryAfterMs ? Date.now() + status.retryAfterMs : 0,
          );
        }
        if (status.logStr) showMessage(status.logStr, status.status === "denied" ? "error" : "info");
        break;
      }

      case "MsgSpectatorHandResponse": {
        const response = msg as MsgSpectatorHandResponse;
        if (response.requestId) useGameStore.getState().removeSpectatorHandRequest(response.requestId);
        if (response.result === false) showMessage(response.logStr ?? "手牌申请处理失败", "error");
        break;
      }

      case "MsgKickSpectator": {
        const response = msg as MsgKickSpectator;
        if (response.result === false) showMessage(response.logStr ?? "移出观战者失败", "error");
        break;
      }

      case "MsgSpectatorKicked": {
        const kicked = msg as MsgSpectatorKicked;
        useNetStore.getState().setSpectate("idle");
        useGameStore.getState().resetGame();
        useNetStore.getState().setNavigateTo("/home");
        showMessage(kicked.logStr ?? "你已被移出观战", "error");
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
