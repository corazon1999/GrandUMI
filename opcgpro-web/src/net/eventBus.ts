import mitt from "mitt";
import type { MsgBase } from "@/types/net";

export type ConnectionState =
  | "disconnected"
  | "connecting"
  | "handshaking"
  | "connected"
  | "reconnecting"
  | "recovering"
  | "failed";

type Events = {
  // 连接事件
  connectSucc: void;        // 首次握手完成，可以登录
  connectFail: void;
  close: void;
  reconnected: void;        // 二次以后的握手完成（重连成功）
  sessionReplaced: { reason: string }; // 同账号在其他设备登录，本会话必须停止自动恢复
  stateChange: ConnectionState;
  reconnectCountdown: number;

  // 消息分发（所有协议消息经此事件分发给各协议处理器）
  message: MsgBase;

  // 游戏事件
  actionRejected: { reason: string };
  opponentDisconnected: { gracePeriodSeconds: number };
  opponentReconnected: void;
  duelOver: { isWin: boolean; description: string };

  // 局内聊天（房间内：双方+观战者）
  gameChat: {
    text: string;
    code?: string | null;
    fromSeat: number;
    fromAccount?: string;
    fromName: string;
    fromRole: "player" | "spectator";
  };
};

export const eventBus = mitt<Events>();
