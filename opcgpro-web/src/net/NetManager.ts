import { eventBus, type ConnectionState } from "./eventBus";
import type { MsgBase, MsgPing } from "@/types/net";

// 客户端版本号，与服务器 MsgSecret 握手时校验
export const CLIENT_VERSION = "0.998";

// WebSocket 服务器地址，通过环境变量配置
const DEFAULT_WS_URL = "ws://localhost:8080/ws";

class NetManagerClass {
  private ws: WebSocket | null = null;
  private pingTimer: ReturnType<typeof setInterval> | null = null;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private reconnectCountdownTimer: ReturnType<typeof setInterval> | null = null;
  private msgQueue: MsgBase[] = [];
  private isProcessing = false;
  private _state: ConnectionState = "disconnected";
  private url = DEFAULT_WS_URL;

  // 重连参数
  private reconnectAttempts = 0;
  private readonly RECONNECT_BASE_DELAY = 1500; // ms
  private readonly RECONNECT_MAX_DELAY = 5000; // 赛前房保留 30 秒，重试间隔不能膨胀到窗口之外
  /// 标记重连：握手完成后会自动触发 reconnected 事件，由 NetProvider 决定是否重新登录 + 请求状态
  private wasConnectedBefore = false;
  private manualClose = false;
  private socketGeneration = 0;
  private lossNotified = false;
  private lastPongAt = 0;

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
    if (["connecting", "handshaking", "connected", "reconnecting", "recovering"].includes(this._state)) {
      return;
    }
    this.url = url;
    this.manualClose = false;
    this.reconnectAttempts = 0;
    this.clearTimers();
    this.openSocket(url);
  }

  private openSocket(url: string) {
    const isReconnectAttempt = this.wasConnectedBefore || this.reconnectAttempts > 0;
    this.setState(isReconnectAttempt ? "reconnecting" : "connecting");

    const generation = ++this.socketGeneration;
    let socket: WebSocket;

    try {
      socket = new WebSocket(url);
      this.ws = socket;
    } catch {
      this.onConnectionFailed();
      return;
    }

    socket.onopen = () => {
      if (!this.isCurrentSocket(socket, generation)) return;
      this.setState(isReconnectAttempt ? "recovering" : "handshaking");
      // 连接后立即发送握手消息（对应 C# ConnectCallback 中的 SecretRequest）
      this.sendOn(socket, { proto: "MsgSecret", vesion: CLIENT_VERSION } as MsgBase);
    };

    socket.onclose = () => {
      if (!this.isCurrentSocket(socket, generation)) return;
      this.ws = null;
      this.stopHeartbeat();
      if (this.manualClose) {
        this.setState("disconnected");
        return;
      }

      if (this.wasConnectedBefore && !this.lossNotified) {
        this.lossNotified = true;
        eventBus.emit("close");
      }
      this.scheduleReconnect();
    };

    socket.onerror = () => {
      if (!this.isCurrentSocket(socket, generation)) return;
      eventBus.emit("connectFail");
    };

    socket.onmessage = (e: MessageEvent<string>) => {
      if (!this.isCurrentSocket(socket, generation)) return;
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
      this.lastPongAt = Date.now();
      return;
    }

    // 握手回包：更新状态后继续分发
    if (msg.proto === "MsgSecret") {
      if (this._state === "handshaking" || this._state === "recovering") {
        const isReconnect = this.wasConnectedBefore;
        this.reconnectAttempts = 0;
        this.wasConnectedBefore = true;
        this.lossNotified = false;
        this.lastPongAt = Date.now();
        this.startHeartbeat();
        this.setState(isReconnect ? "recovering" : "connected");
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
    return this.sendOn(this.ws, msg);
  }

  private sendOn(socket: WebSocket, msg: MsgBase) {
    try {
      socket.send(JSON.stringify(msg));
      return true;
    } catch {
      console.warn("[NetManager] 发送异常:", msg.proto);
      return false;
    }
  }

  finishRecovery() {
    if (this._state === "recovering") this.setState("connected");
  }

  disconnect() {
    this.clearTimers();
    this.manualClose = true;
    this.reconnectAttempts = 0;
    this.wasConnectedBefore = false;
    const socket = this.ws;
    this.ws = null;
    this.socketGeneration++;
    socket?.close(1000, "主动断开");
    this.setState("disconnected");
  }

  private onConnectionFailed() {
    eventBus.emit("connectFail");
    this.scheduleReconnect();
  }

  private scheduleReconnect() {
    this.clearReconnectTimers();
    // 持续重试并封顶间隔，避免网络在房间宽限期内恢复后，客户端却因长退避或停止重试而必须刷新。
    const delay = Math.min(
      this.RECONNECT_BASE_DELAY * Math.pow(2, Math.min(this.reconnectAttempts, 8)),
      this.RECONNECT_MAX_DELAY,
    );
    this.reconnectAttempts++;
    console.log(`[NetManager] ${delay / 1000}s 后尝试第 ${this.reconnectAttempts} 次重连`);
    this.setState("reconnecting");
    eventBus.emit("reconnectCountdown", Math.ceil(delay / 1000));

    let remaining = Math.ceil(delay / 1000);
    this.reconnectCountdownTimer = setInterval(() => {
      remaining = Math.max(0, remaining - 1);
      eventBus.emit("reconnectCountdown", remaining);
    }, 1000);

    this.reconnectTimer = setTimeout(() => {
      this.clearReconnectTimers();
      this.openSocket(this.url);
    }, delay);
  }

  private startHeartbeat() {
    this.stopHeartbeat();
    const socket = this.ws;
    if (!socket) return;
    this.pingTimer = setInterval(() => {
      if (this.ws !== socket || socket.readyState !== WebSocket.OPEN) return;
      if (Date.now() - this.lastPongAt > 30_000) {
        console.warn("[NetManager] 心跳超时，主动重建连接");
        socket.close(4000, "心跳超时");
        return;
      }
      this.sendOn(socket, { proto: "MsgPing" } as MsgPing);
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
    this.clearReconnectTimers();
  }

  private clearReconnectTimers() {
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
    if (this.reconnectCountdownTimer) {
      clearInterval(this.reconnectCountdownTimer);
      this.reconnectCountdownTimer = null;
    }
  }

  private isCurrentSocket(socket: WebSocket, generation: number) {
    return this.ws === socket && this.socketGeneration === generation;
  }

  private setState(state: ConnectionState) {
    this._state = state;
    eventBus.emit("stateChange", state);
  }
}

export const NetManager = new NetManagerClass();
