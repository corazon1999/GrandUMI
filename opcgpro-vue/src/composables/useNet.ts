import { onScopeDispose } from "vue";
import { NetManager } from "@/net/NetManager";
import { registerHomeProtocols } from "@/net/HomeProtocol";
import { registerGameProtocols } from "@/net/GameProtocol";
import { eventBus } from "@/net/eventBus";
import { useNetStore } from "@/store/netStore";

/**
 * useNet — 等价旧项目的 NetProvider 副作用：
 *   启动时注册协议处理器（仅一次）+ 连接 WebSocket + 同步连接状态到 netStore。
 * 在 App.vue setup 内调用一次即可。
 *
 * WS 地址：`VITE_WS_URL`（Vite 环境变量），缺省回退 `ws://localhost:8080/ws`。
 */

const WS_URL = import.meta.env.VITE_WS_URL ?? "ws://localhost:8080/ws";

let protocolsRegistered = false;

export function useNet() {
  if (!protocolsRegistered) {
    registerHomeProtocols();
    registerGameProtocols();
    protocolsRegistered = true;
  }

  // stateChange 事件与 eventBus Events 中的 ConnectionState 一致
  const onStateChange = (
    state: "disconnected" | "connecting" | "handshaking" | "connected",
  ) => {
    useNetStore.getState().setConnState(state);
  };

  eventBus.on("stateChange", onStateChange);
  NetManager.connect(WS_URL);

  onScopeDispose(() => {
    eventBus.off("stateChange", onStateChange);
  });
}
