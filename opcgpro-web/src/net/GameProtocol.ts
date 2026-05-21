/**
 * GameProtocol.ts — 游戏协议接收层
 * 对应 C# GameProtocol.cs（ProtocolManager 的游戏部分）
 *
 * 架构原则（重构方案 §5.6）：
 *   只负责接收服务器消息并转发给 store，无任何本地结算逻辑
 *   唯一写入入口：MsgGameState → gameStore.syncFromServer()
 */

import { eventBus } from "./eventBus";
import type {
  MsgBase,
  MsgGameStart,
  MsgShuffle,
  MsgTransmit,
  MsgDuelOver,
  MsgGameState,
  MsgPlayerDisconnected,
  MsgPlayerReconnected,
} from "@/types/net";
import { useGameStore } from "@/store/gameStore";
import { useNetStore } from "@/store/netStore";
import { GameRequest } from "./GameRequest";

let registered = false;

export function registerGameProtocols() {
  if (registered) return;
  registered = true;

  eventBus.on("message", (msg: MsgBase) => {
    switch (msg.proto) {
      case "MsgGameStart":
        // MsgGameStart 由 HomeProtocol 处理场景跳转，此处不重复处理
        break;

      case "MsgShuffle":
        handleShuffle(msg as MsgShuffle);
        break;

      // ── 当前协议（MsgTransmit 字符串透传）───────────────────────────
      case "MsgTransmit":
        handleTransmit(msg as MsgTransmit);
        break;

      // ── 新服务端结算协议 ─────────────────────────────────────────────
      case "MsgGameState":
        handleGameState(msg as MsgGameState);
        break;

      case "MsgPlayerDisconnected":
        handlePlayerDisconnected(msg as MsgPlayerDisconnected);
        break;

      case "MsgPlayerReconnected":
        handlePlayerReconnected(msg as MsgPlayerReconnected);
        break;

      // ── 游戏结束 ─────────────────────────────────────────────────────
      case "MsgDuelOver":
        handleDuelOver(msg as MsgDuelOver);
        break;
    }
  });
}

// ── 处理器实现 ──────────────────────────────────────────────────────────

function handleShuffle(msg: MsgShuffle) {
  if (msg.MainDeck && typeof window !== "undefined") {
    sessionStorage.setItem("shuffledDeck", msg.MainDeck);
  }
}

/**
 * MsgTransmit — 游戏操作透传（当前协议，兼容模式）
 * C#: AcceptManager.Instance.GetRequest(msg.Msg)
 * Msg 是 RequestManager 序列化的操作字符串，格式: "ActionName^param1^param2^..."
 *
 * TODO: 待服务器全面切换到 MsgGameState 后移除此分支
 */
function handleTransmit(msg: MsgTransmit) {
  if (!msg.Msg) return;

  // 换牌完成同步
  if (msg.Msg === "ReDrawFinish") {
    useGameStore.getState().setOpponentMulliganDone();
    console.debug("[GameProtocol] 对手换牌完成");
    return;
  }

  // 解析操作字符串（格式: "ActionName^param1^param2^..."）
  const action = msg.Msg.split("^")[0];

  // 对手结束回合 → 本地切换回合
  if (action === "EndTurn") {
    useGameStore.getState().nextTurn();
    // 若现在是我的回合且不是第一轮（双方首轮均不抽牌），抽一张牌
    const s = useGameStore.getState();
    if (s.currentTurn && s.turnCount >= 3) {
      GameRequest.drawForTurn();
    }
    console.debug("[GameProtocol] 对手结束回合");
    return;
  }

  // 对手出牌
  if (action === "AppearFromHand") {
    useGameStore.getState().executeAction(msg.Msg, true);
    console.debug("[GameProtocol] 对手出牌:", msg.Msg.slice(0, 80));
    return;
  }

  // 对手抽牌
  if (action === "Draw") {
    useGameStore.getState().executeAction(msg.Msg, true);
    console.debug("[GameProtocol] 对手抽牌:", msg.Msg.slice(0, 40));
    return;
  }

  // 对手附加咚
  if (action === "AddDong") {
    useGameStore.getState().executeAction(msg.Msg, true);
    console.debug("[GameProtocol] 对手附加咚:", msg.Msg.slice(0, 50));
    return;
  }

  // 其余操作通过 eventBus 分发给 AcceptManager 兼容层
  eventBus.emit("gameAction" as never, msg.Msg as never);
  console.debug("[GameProtocol] MsgTransmit:", msg.Msg.slice(0, 80));
}

/**
 * MsgGameState — 服务器权威游戏快照（新协议，重构方案目标态）
 *
 * 数据流：
 *   服务器结算完成 → BroadcastGameState → MsgGameState
 *   → gameStore.syncFromServer() → immer 逐字段赋值 → UI 重渲染
 *   → lastAction 字段触发对应动画
 */
function handleGameState(msg: MsgGameState) {
  useGameStore.getState().syncFromServer(msg);
}

/**
 * MsgPlayerDisconnected — 对手断线通知
 * 服务器宽限期 90 秒，前端显示倒计时横幅
 */
function handlePlayerDisconnected(msg: MsgPlayerDisconnected) {
  eventBus.emit("opponentDisconnected" as never, {
    gracePeriodSeconds: msg.gracePeriodSeconds,
  } as never);
}

/**
 * MsgPlayerReconnected — 对手重连通知
 */
function handlePlayerReconnected(_msg: MsgPlayerReconnected) {
  eventBus.emit("opponentReconnected" as never);
}

/**
 * MsgDuelOver — 游戏结束
 */
function handleDuelOver(msg: MsgDuelOver) {
  useGameStore.getState().setGameOver(true);
  useNetStore.getState().setMatchState("idle");
  useNetStore.getState().setOpponentName("");
  eventBus.emit("duelOver" as never, {
    isWin: msg.IsWin,
    description: msg.Description,
  } as never);
  console.log(
    `[GameProtocol] 游戏结束 — ${msg.IsWin ? "胜利" : "失败"}: ${msg.Description}`,
  );
}
