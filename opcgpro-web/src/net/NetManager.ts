import { eventBus, type ConnectionState } from "./eventBus";
import type { MsgBase, MsgGameState, MsgGameStateDelta, MsgPing, MsgRequestState } from "@/types/net";

// 客户端版本号，与服务器 MsgSecret 握手时校验
export const CLIENT_VERSION = "0.999";

// WebSocket 服务器地址，通过环境变量配置
const DEFAULT_WS_URL = "ws://localhost:8080/ws";

export interface NetworkDiagnostics {
  endpointHost: string;
  handshakeMs: number | null;
  reconnectCount: number;
  endpointFailureCount: number;
  lastDisconnectReason: string | null;
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

interface EndpointHealth {
  consecutiveFailures: number;
  totalFailures: number;
  circuitOpenUntil: number;
  lastSuccessAt: number;
}

class NetManagerClass {
  private ws: WebSocket | null = null;
  private pingTimer: ReturnType<typeof setInterval> | null = null;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private reconnectCountdownTimer: ReturnType<typeof setInterval> | null = null;
  private connectionTimeoutTimer: ReturnType<typeof setTimeout> | null = null;
  private msgQueue: MsgBase[] = [];
  private isProcessing = false;
  private _state: ConnectionState = "disconnected";
  private url = DEFAULT_WS_URL;
  private endpoints = [DEFAULT_WS_URL];
  private endpointIndex = 0;
  private endpointFailuresInCycle = 0;
  private endpointHealth = new Map<string, EndpointHealth>();
  private connectionStartedAt = 0;
  private handshakeMs: number | null = null;
  private reconnectCount = 0;
  private lastDisconnectReason: string | null = null;
  private rttReportedGeneration = 0;

  // 重连参数
  private reconnectAttempts = 0;
  private readonly RECONNECT_BASE_DELAY = 1500; // ms
  private readonly RECONNECT_MAX_DELAY = 5000; // 赛前房保留 30 秒，重试间隔不能膨胀到窗口之外
  private readonly CONNECTION_TIMEOUT_MS = 5_000;
  // 正式服首选直连通常数百毫秒即可完成握手。它发生故障时应尽快切到备用线路，
  // 不能让玩家每轮都先被坏线路完整阻塞五秒。
  private readonly FALLBACK_SWITCH_TIMEOUT_MS = 1_500;
  private readonly CIRCUIT_FAILURE_THRESHOLD = 2;
  private readonly CIRCUIT_OPEN_MS = 45_000;
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
    const health = this.getEndpointHealth(this.url);
    return {
      endpointHost: endpointHost(this.url),
      handshakeMs: this.handshakeMs,
      reconnectCount: this.reconnectCount,
      endpointFailureCount: health.totalFailures,
      lastDisconnectReason: this.lastDisconnectReason,
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

  /** 新对局开始时丢弃上一局的增量快照基线，等待服务端首份完整状态。 */
  resetGameStateBaseline() {
    this.stateBaseline = null;
    this.deltaResyncRequested = false;
  }

  connect(url: string | readonly string[] = DEFAULT_WS_URL) {
    if (["connecting", "handshaking", "connected", "reconnecting", "recovering"].includes(this._state)) {
      return;
    }
    this.endpoints = this.rankEndpoints(normalizeEndpoints(url));
    this.endpointIndex = 0;
    this.endpointFailuresInCycle = 0;
    this.url = this.endpoints[0];
    this.manualClose = false;
    this.reconnectAttempts = 0;
    this.clearTimers();
    this.openSocket(this.url);
  }

  /**
   * 跳过当前等待并立即换一条线路重试。
   * 用于浏览器恢复联网以及玩家主动重试；旧连接通过 generation 失效，
   * 它随后触发的 close/error 不会再启动第二套重连计时器。
   */
  retryNow(url?: string | readonly string[]) {
    if (this._state === "connected" || this._state === "recovering") return;
    if (url !== undefined) this.endpoints = this.rankEndpoints(normalizeEndpoints(url));

    this.clearTimers();
    const socket = this.ws;
    this.ws = null;
    this.socketGeneration++;
    try {
      socket?.close(4002, "立即切换线路重连");
    } catch {
      // 浏览器可能已经回收异常连接；继续建立新连接即可。
    }

    this.manualClose = false;
    this.endpointFailuresInCycle = 0;
    this.endpointIndex = (this.endpointIndex + 1) % this.endpoints.length;
    this.url = this.endpoints[this.endpointIndex];
    this.openSocket(this.url);
  }

  private openSocket(url: string) {
    const isReconnectAttempt = this.wasConnectedBefore || this.reconnectAttempts > 0;
    this.setState(isReconnectAttempt ? "reconnecting" : "connecting");

    const generation = ++this.socketGeneration;
    this.connectionStartedAt = now();
    this.resetConnectionMeasurements();
    let socket: WebSocket;

    try {
      socket = new WebSocket(url);
      this.ws = socket;
    } catch {
      this.onConnectionFailed();
      return;
    }

    this.startConnectionTimeout(socket, generation);

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

    socket.onclose = (event) => {
      if (!this.isCurrentSocket(socket, generation)) return;
      this.ws = null;
      this.clearConnectionTimeout();
      this.stopHeartbeat();
      this.stateBaseline = null;
      this.pendingPings.clear();
      this.lastDisconnectReason = event.reason || `WebSocket ${event.code}`;
      if (this.manualClose) {
        this.setState("disconnected");
        return;
      }

      if (this.wasConnectedBefore && !this.lossNotified) {
        this.lossNotified = true;
        eventBus.emit("close");
      }
      this.onConnectionFailed();
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
          if (this.rttReportedGeneration !== this.socketGeneration) {
            this.rttReportedGeneration = this.socketGeneration;
            this.reportNetworkDiagnostics();
          }
          if (rtt >= 80) console.info(`[延迟] WebSocket RTT ${rtt.toFixed(1)}ms`);
        }
      }
      return;
    }

    // 握手回包：更新状态后继续分发
    if (msg.proto === "MsgSecret") {
      if (this._state === "handshaking" || this._state === "recovering") {
        this.clearConnectionTimeout();
        this.endpointFailuresInCycle = 0;
        const isReconnect = this.wasConnectedBefore;
        this.handshakeMs = Math.max(0, now() - this.connectionStartedAt);
        this.markEndpointSuccess(this.url);
        if (isReconnect) this.reconnectCount++;
        this.reconnectAttempts = 0;
        this.wasConnectedBefore = true;
        this.lossNotified = false;
        this.lastPongAt = Date.now();
        this.stateDeltaEnabled = Boolean((msg as { stateDeltaEnabled?: boolean }).stateDeltaEnabled);
        this.startHeartbeat();
        this.setState(isReconnect ? "recovering" : "connected");
        eventBus.emit("connectSucc");
        if (isReconnect) eventBus.emit("reconnected");
        this.reportNetworkDiagnostics();
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

  /** 浏览器从后台或休眠恢复时立即验证当前连接，坏连接不再等待下一轮定时心跳。 */
  handleForegroundResume(url?: string | readonly string[]) {
    if (url !== undefined) this.endpoints = this.rankEndpoints(normalizeEndpoints(url));
    const socket = this.ws;
    if (socket?.readyState === WebSocket.OPEN && this.isConnected) {
      if (Date.now() - this.lastPongAt > 20_000) {
        socket.close(4003, "页面恢复时心跳已过期");
      } else {
        this.sendHeartbeat(socket);
      }
      return;
    }
    this.retryNow(this.endpoints);
  }

  disconnect() {
    this.clearTimers();
    this.manualClose = true;
    this.reconnectAttempts = 0;
    this.endpointFailuresInCycle = 0;
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
    this.markEndpointFailure(this.url);
    eventBus.emit("connectFail");
    if (this.endpointFailuresInCycle < this.endpoints.length - 1) {
      this.endpointFailuresInCycle++;
      this.endpointIndex = this.findNextEndpointIndex();
      this.url = this.endpoints[this.endpointIndex];
      console.warn(`[NetManager] 当前线路不可用，切换备用线路：${this.url}`);
      this.openSocket(this.url);
      return;
    }

    this.endpointFailuresInCycle = 0;
    this.endpointIndex = this.findNextEndpointIndex();
    this.url = this.endpoints[this.endpointIndex];
    this.scheduleReconnect();
  }

  private startConnectionTimeout(socket: WebSocket, generation: number) {
    this.clearConnectionTimeout();
    const hasUnusedFallback = this.endpointFailuresInCycle < this.endpoints.length - 1;
    const timeoutMs = hasUnusedFallback
      ? this.FALLBACK_SWITCH_TIMEOUT_MS
      : this.CONNECTION_TIMEOUT_MS;
    this.connectionTimeoutTimer = setTimeout(() => {
      if (!this.isCurrentSocket(socket, generation)) return;
      console.warn(`[NetManager] 连接握手超时，准备切换线路：${this.url}`);
      this.ws = null;
      this.socketGeneration++;
      this.connectionTimeoutTimer = null;
      try {
        socket.close(4001, "连接握手超时");
      } finally {
        this.onConnectionFailed();
      }
    }, timeoutMs);
  }

  private scheduleReconnect() {
    this.clearReconnectTimers();
    // 持续重试并封顶间隔，避免网络在房间宽限期内恢复后，客户端却因长退避或停止重试而必须刷新。
    const baseDelay = Math.min(
      this.RECONNECT_BASE_DELAY * Math.pow(2, Math.min(this.reconnectAttempts, 8)),
      this.RECONNECT_MAX_DELAY,
    );
    // 加入抖动，避免入口短暂恢复时大量玩家在同一毫秒重连形成惊群。
    const delay = Math.round(baseDelay * (0.75 + Math.random() * 0.5));
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
    this.sendHeartbeat(socket);
    this.pingTimer = setInterval(() => {
      if (this.ws !== socket || socket.readyState !== WebSocket.OPEN) return;
      if (Date.now() - this.lastPongAt > 30_000) {
        console.warn("[NetManager] 心跳超时，主动重建连接");
        socket.close(4000, "心跳超时");
        return;
      }
      this.sendHeartbeat(socket);
    }, 10_000);
  }

  private sendHeartbeat(socket: WebSocket) {
    if (this.ws !== socket || socket.readyState !== WebSocket.OPEN) return;
    const id = `${this.socketGeneration}-${++this.pingSequence}`;
    const sentAt = now();
    if (this.sendOn(socket, { proto: "MsgPing", id } as MsgPing)) {
      this.pendingPings.set(id, sentAt);
      for (const [pendingId, pendingAt] of this.pendingPings) {
        if (sentAt - pendingAt > 30_000) this.pendingPings.delete(pendingId);
      }
    }
  }

  private getEndpointHealth(url: string): EndpointHealth {
    let health = this.endpointHealth.get(url);
    if (!health) {
      health = { consecutiveFailures: 0, totalFailures: 0, circuitOpenUntil: 0, lastSuccessAt: 0 };
      this.endpointHealth.set(url, health);
    }
    return health;
  }

  private markEndpointFailure(url: string) {
    const health = this.getEndpointHealth(url);
    health.consecutiveFailures++;
    health.totalFailures++;
    if (health.consecutiveFailures >= this.CIRCUIT_FAILURE_THRESHOLD) {
      health.circuitOpenUntil = Date.now() + this.CIRCUIT_OPEN_MS;
    }
  }

  private markEndpointSuccess(url: string) {
    const health = this.getEndpointHealth(url);
    health.consecutiveFailures = 0;
    health.circuitOpenUntil = 0;
    health.lastSuccessAt = Date.now();
    try { localStorage.setItem("grandumi_last_good_ws", url); } catch { /* 隐私模式可禁用存储。 */ }
  }

  private rankEndpoints(endpoints: string[]): string[] {
    let preferred = "";
    try { preferred = localStorage.getItem("grandumi_last_good_ws") ?? ""; } catch { /* noop */ }
    return [...endpoints].sort((a, b) => {
      const aOpen = this.getEndpointHealth(a).circuitOpenUntil > Date.now() ? 1 : 0;
      const bOpen = this.getEndpointHealth(b).circuitOpenUntil > Date.now() ? 1 : 0;
      if (aOpen !== bOpen) return aOpen - bOpen;
      if (a === preferred) return -1;
      if (b === preferred) return 1;
      return this.getEndpointHealth(b).lastSuccessAt - this.getEndpointHealth(a).lastSuccessAt;
    });
  }

  private findNextEndpointIndex(): number {
    const nowMs = Date.now();
    for (let offset = 1; offset <= this.endpoints.length; offset++) {
      const candidate = (this.endpointIndex + offset) % this.endpoints.length;
      if (this.getEndpointHealth(this.endpoints[candidate]).circuitOpenUntil <= nowMs) return candidate;
    }
    // 全部熔断时仍选择最早允许半开探测的一条，避免永久停止恢复。
    let best = 0;
    for (let index = 1; index < this.endpoints.length; index++) {
      if (this.getEndpointHealth(this.endpoints[index]).circuitOpenUntil
          < this.getEndpointHealth(this.endpoints[best]).circuitOpenUntil) best = index;
    }
    return best;
  }

  private reportNetworkDiagnostics() {
    const diagnostics = this.getDiagnostics();
    this.send({
      proto: "MsgNetworkDiagnostics",
      endpointHost: diagnostics.endpointHost,
      handshakeMs: diagnostics.handshakeMs,
      rttMs: diagnostics.rttMs,
      rttP95Ms: diagnostics.rttP95Ms,
      reconnectCount: diagnostics.reconnectCount,
      endpointFailureCount: diagnostics.endpointFailureCount,
      lastDisconnectReason: diagnostics.lastDisconnectReason?.slice(0, 120) ?? null,
    } as MsgBase);
  }

  private stopHeartbeat() {
    if (this.pingTimer) {
      clearInterval(this.pingTimer);
      this.pingTimer = null;
    }
  }

  private clearTimers() {
    this.clearConnectionTimeout();
    this.stopHeartbeat();
    this.clearReconnectTimers();
  }

  private clearConnectionTimeout() {
    if (this.connectionTimeoutTimer) {
      clearTimeout(this.connectionTimeoutTimer);
      this.connectionTimeoutTimer = null;
    }
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

function normalizeEndpoints(url: string | readonly string[]): string[] {
  const endpoints = (typeof url === "string" ? [url] : url).filter(Boolean);
  return [...new Set(endpoints.length > 0 ? endpoints : [DEFAULT_WS_URL])];
}

function endpointHost(raw: string): string {
  try { return new URL(raw).host; } catch { return "unknown"; }
}

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
