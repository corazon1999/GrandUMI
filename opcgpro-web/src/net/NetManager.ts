import { eventBus, type ConnectionState } from "./eventBus";
import type { MsgBase, MsgGameState, MsgGameStateDelta, MsgPing, MsgRequestState } from "@/types/net";

// 客户端版本号，与服务器 MsgSecret 握手时校验
export const CLIENT_VERSION = "0.999";

// WebSocket 服务器地址，通过环境变量配置
const DEFAULT_WS_URL = "ws://localhost:8080/ws";

export interface NetworkDiagnostics {
  rttMs: number | null;
  rttP95Ms: number | null;
  parseMaxMs: number;
  handlerMaxMs: number;
  actionRoundTripMs: number | null;
  actionRoundTripP95Ms: number | null;
  maxMessageQueueDepth: number;
  stateDeltaEnabled: boolean;
  stateDeltaCount: number;
  fullStateCount: number;
}

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
  private pingSequence = 0;
  private pendingPings = new Map<string, number>();
  private rttSamples: number[] = [];
  private actionLatencySamples: number[] = [];
  private parseMaxMs = 0;
  private handlerMaxMs = 0;
  private maxMessageQueueDepth = 0;
  private stateBaseline: MsgGameState | null = null;
  private deltaResyncRequested = false;
  private stateDeltaEnabled = false;
  private stateDeltaCount = 0;
  private fullStateCount = 0;

  get state(): ConnectionState {
    return this._state;
  }

  get isConnected(): boolean {
    return this._state === "connected";
  }

  get isHandshaking(): boolean {
    return this._state === "handshaking";
  }

  getDiagnostics(): NetworkDiagnostics {
    return {
      rttMs: this.rttSamples.at(-1) ?? null,
      rttP95Ms: percentile(this.rttSamples, 0.95),
      parseMaxMs: this.parseMaxMs,
      handlerMaxMs: this.handlerMaxMs,
      actionRoundTripMs: this.actionLatencySamples.at(-1) ?? null,
      actionRoundTripP95Ms: percentile(this.actionLatencySamples, 0.95),
      maxMessageQueueDepth: this.maxMessageQueueDepth,
      stateDeltaEnabled: this.stateDeltaEnabled,
      stateDeltaCount: this.stateDeltaCount,
      fullStateCount: this.fullStateCount,
    };
  }

  recordActionLatency(elapsedMs: number) {
    pushBounded(this.actionLatencySamples, elapsedMs);
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
    this.resetConnectionMeasurements();
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
      this.sendOn(socket, {
        proto: "MsgSecret",
        vesion: CLIENT_VERSION,
        supportsStateDelta: true,
      } as MsgBase);
    };

    socket.onclose = () => {
      if (!this.isCurrentSocket(socket, generation)) return;
      this.ws = null;
      this.stopHeartbeat();
      this.stateBaseline = null;
      this.pendingPings.clear();
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
      const parseStartedAt = now();
      try {
        let msg = JSON.parse(e.data) as MsgBase;
        const parseElapsed = now() - parseStartedAt;
        this.parseMaxMs = Math.max(this.parseMaxMs, parseElapsed);
        if (parseElapsed >= 8) {
          console.info(`[延迟] WebSocket JSON 解析 ${parseElapsed.toFixed(1)}ms，字节=${e.data.length}`);
        }

        if (msg.proto === "MsgGameStateDelta") {
          const materialized = this.materializeStateDelta(socket, msg as MsgGameStateDelta);
          if (!materialized) return;
          msg = materialized;
          this.stateDeltaCount++;
        } else if (msg.proto === "MsgGameState") {
          this.stateBaseline = msg as MsgGameState;
          this.deltaResyncRequested = false;
          this.fullStateCount++;
        }
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
      const ping = msg as MsgPing;
      if (ping.id) {
        const startedAt = this.pendingPings.get(ping.id);
        this.pendingPings.delete(ping.id);
        if (startedAt !== undefined) {
          const rtt = now() - startedAt;
          pushBounded(this.rttSamples, rtt);
          if (rtt >= 80) console.info(`[延迟] WebSocket RTT ${rtt.toFixed(1)}ms`);
        }
      }
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
        this.stateDeltaEnabled = Boolean((msg as { stateDeltaEnabled?: boolean }).stateDeltaEnabled);
        this.startHeartbeat();
        this.setState(isReconnect ? "recovering" : "connected");
        eventBus.emit("connectSucc");
        if (isReconnect) eventBus.emit("reconnected");
      }
    }

    // 所有消息（含 MsgSecret）推入队列分发给协议处理器
    this.msgQueue.push(msg);
    this.maxMessageQueueDepth = Math.max(this.maxMessageQueueDepth, this.msgQueue.length);
    this.flushQueue();
  }

  private flushQueue() {
    if (this.isProcessing) return;
    this.isProcessing = true;
    while (this.msgQueue.length > 0) {
      const batch = this.msgQueue;
      this.msgQueue = [];
      for (const msg of batch) {
        const handlerStartedAt = now();
        try {
          eventBus.emit("message", msg);
        } catch (err) {
          console.error("[NetManager] 协议处理异常:", err);
        } finally {
          const elapsed = now() - handlerStartedAt;
          this.handlerMaxMs = Math.max(this.handlerMaxMs, elapsed);
          if (elapsed >= 16) console.info(`[延迟] 协议处理 ${msg.proto} ${elapsed.toFixed(1)}ms`);
        }
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
    this.stateBaseline = null;
    this.pendingPings.clear();
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
      const id = `${this.socketGeneration}-${++this.pingSequence}`;
      const sentAt = now();
      if (this.sendOn(socket, { proto: "MsgPing", id } as MsgPing)) {
        this.pendingPings.set(id, sentAt);
        for (const [pendingId, pendingAt] of this.pendingPings) {
          if (sentAt - pendingAt > 30_000) this.pendingPings.delete(pendingId);
        }
      }
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

  private materializeStateDelta(socket: WebSocket, delta: MsgGameStateDelta): MsgGameState | null {
    const baseline = this.stateBaseline;
    if (!baseline || baseline.tick !== delta.baseTick) {
      if (!this.deltaResyncRequested) {
        this.deltaResyncRequested = true;
        this.sendOn(socket, { proto: "MsgRequestState" } as MsgRequestState);
      }
      console.warn(`[NetManager] 增量快照基线不匹配，本地=${baseline?.tick ?? "无"}，服务端=${delta.baseTick}`);
      return null;
    }

    const changes = delta.changes;
    const next = {
      ...baseline,
      ...changes,
      proto: "MsgGameState" as const,
      tick: delta.tick,
      my: changes.my ? { ...baseline.my, ...changes.my } : baseline.my,
      opponent: changes.opponent ? { ...baseline.opponent, ...changes.opponent } : baseline.opponent,
    } as MsgGameState;
    this.stateBaseline = next;
    return next;
  }

  private resetConnectionMeasurements() {
    this.stateBaseline = null;
    this.deltaResyncRequested = false;
    this.stateDeltaEnabled = false;
    this.pendingPings.clear();
    this.pingSequence = 0;
  }
}

export const NetManager = new NetManagerClass();

function now(): number {
  return typeof performance === "undefined" ? Date.now() : performance.now();
}

function pushBounded(values: number[], value: number, limit = 120) {
  values.push(value);
  if (values.length > limit) values.splice(0, values.length - limit);
}

function percentile(values: number[], ratio: number): number | null {
  if (values.length === 0) return null;
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.min(sorted.length - 1, Math.ceil(sorted.length * ratio) - 1)];
}
