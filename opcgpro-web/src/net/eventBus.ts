import mitt from "mitt";
import type { MsgBase } from "@/types/net";

type ConnectionState = "disconnected" | "connecting" | "handshaking" | "connected";

type Events = {
  // 连接事件
  connectSucc: void;   // 握手完成，可以登录
  connectFail: void;   // 连接失败
  close: void;         // 连接关闭
  stateChange: ConnectionState;

  // 消息分发（所有协议消息经此事件分发给各协议处理器）
  message: MsgBase;
};

export const eventBus = mitt<Events>();
