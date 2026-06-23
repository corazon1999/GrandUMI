import { eventBus } from "./eventBus";
import type { MsgBase, MsgPing } from "@/types/net";

// 客户端版本号，与服务器 MsgSecret 握手时校验
export const CLIENT_VERSION = "0.998";

// WebSocket 服务器地址，通过环境变量配置
const DEFAULT_WS_URL = "ws://localhost:8080/ws";

type ConnectionState =
  | "disconnected"
  | "connecting"
  | "handshaking" // 已连接，等待 MsgSecret 握手完成
  | "connected";  // 握手成功，可正常收发消息

class NetManagerClass {
  private ws: WebSocket | null = null;
  private pingTimer: ReturnType<typeof setInterval> | null = null;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private msgQueue: MsgBase[] = [];
  private isProcessing = false;
  private _state: ConnectionState = "disconnected";
  private url = DEFAULT_WS_URL;

  // 重连参数
  private reconnectAttempts = 0;
  private readonly MAX_RECONNECT = 6;
  private readonly RECONNECT_BASE_DELAY = 1500; // ms
  /// 标记重连：握手完成后会自动触发 reconnected 事件，由 NetProvider 决定是否重新登录 + 请求状态
  private wasConnectedBefore = false;

  get state(): ConnectionState {
    return this._state;
  }

  get isConnected(): boolean {
    return this._state === "connected";
  }

  get isHandshaking(): boolean {
    return this._state === "handshaking";
  }

  connect(url: string = DEFAULT_WS_URL) {
    if (this._state === "connecting" || this._state === "handshaking") {
      return;
    }
    this.url = url;
    this.clearTimers();
    this.openSocket(url);
  }

  private openSocket(url: string) {
    this._state = "connecting";
    eventBus.emit("stateChange", "connecting");

    try {
      this.ws = new WebSocket(url);
    } catch {
      this.onConnectionFailed();
      return;
    }

    this.ws.onopen = () => {
      this._state = "handshaking";
      eventBus.emit("stateChange", "handshaking");
      // 连接后立即发送握手消息（对应 C# ConnectCallback 中的 SecretRequest）
      this.sendRaw({ proto: "MsgSecret", vesion: CLIENT_VERSION } as MsgBase);
    };

    this.ws.onclose = (ev) => {
      const wasConnected = this._state === "connected" || this._state === "handshaking";
      this._state = "disconnected";
      this.stopHeartbeat();
      eventBus.emit("stateChange", "disconnected");
      eventBus.emit("close");

      // 非主动关闭时自动重连
      if (wasConnected && !ev.wasClean) {
        this.scheduleReconnect();
      }
    };

    this.ws.onerror = () => {
      // onerror 后一定会触发 onclose，在 onclose 中处理重连
      eventBus.emit("connectFail");
    };

    this.ws.onmessage = (e: MessageEvent<string>) => {
      try {
        const msg = JSON.parse(e.data) as MsgBase;
        this.onMessage(msg);
      } catch {
        console.warn("[NetManager] 消息解析失败:", e.data.slice(0, 200));
      }
    };
  }

  private onMessage(msg: MsgBase) {
    // 心跳回包：直接处理，不入队
    if (msg.proto === "MsgPing") {
      return;
    }

    // 握手回包：更新状态后继续分发
    if (msg.proto === "MsgSecret") {
      if (this._state === "handshaking") {
        const isReconnect = this.wasConnectedBefore && this.reconnectAttempts > 0;
        this._state = "connected";
        this.reconnectAttempts = 0;
        this.wasConnectedBefore = true;
        this.startHeartbeat();
        eventBus.emit("stateChange", "connected");
        eventBus.emit("connectSucc");
        if (isReconnect) eventBus.emit("reconnected");
      }
    }

    // 所有消息（含 MsgSecret）推入队列分发给协议处理器
    this.msgQueue.push(msg);
    this.flushQueue();
  }

  private flushQueue() {
    if (this.isProcessing) return;
    this.isProcessing = true;
    while (this.msgQueue.length > 0) {
      const msg = this.msgQueue.shift()!;
      try {
        eventBus.emit("message", msg);
      } catch (err) {
        console.error("[NetManager] 协议处理异常:", err);
      }
    }
    this.isProcessing = false;
  }

  // 发送消息（已连接或握手中均可发送）
  send(msg: MsgBase) {
    if (this.ws?.readyState !== WebSocket.OPEN) {
      console.warn("[NetManager] 发送失败，未连接:", msg.proto);
      return false;
    }
    this.sendRaw(msg);
    return true;
  }

  private sendRaw(msg: MsgBase) {
    this.ws!.send(JSON.stringify(msg));
  }

  disconnect() {
    this.clearTimers();
    this.reconnectAttempts = this.MAX_RECONNECT; // 阻止自动重连
    this.wasConnectedBefore = false;
    this.ws?.close(1000, "主动断开");
    this.ws = null;
    this._state = "disconnected";
  }

  private onConnectionFailed() {
    this._state = "disconnected";
    eventBus.emit("connectFail");
    this.scheduleReconnect();
  }

  private scheduleReconnect() {
    if (this.reconnectAttempts >= this.MAX_RECONNECT) {
      console.warn("[NetManager] 已达最大重连次数，停止重连");
      return;
    }
    // 指数退避：2s, 4s, 8s, 16s, 32s
    const delay = this.RECONNECT_BASE_DELAY * Math.pow(2, this.reconnectAttempts);
    this.reconnectAttempts++;
    console.log(`[NetManager] ${delay / 1000}s 后尝试第 ${this.reconnectAttempts} 次重连`);

    this.reconnectTimer = setTimeout(() => {
      this.openSocket(this.url);
    }, delay);
  }

  private startHeartbeat() {
    this.stopHeartbeat();
    // 对战中更密：10 秒
    this.pingTimer = setInterval(() => {
      this.sendRaw({ proto: "MsgPing" } as MsgPing);
    }, 10_000);
  }

  private stopHeartbeat() {
    if (this.pingTimer) {
      clearInterval(this.pingTimer);
      this.pingTimer = null;
    }
  }

  private clearTimers() {
    this.stopHeartbeat();
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
  }
}

export const NetManager = new NetManagerClass();
