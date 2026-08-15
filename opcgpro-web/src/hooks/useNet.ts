"use client";

import { useEffect } from "react";
import { NetManager } from "@/net/NetManager";
import { registerHomeProtocols } from "@/net/HomeProtocol";
import { registerGameProtocols } from "@/net/GameProtocol";
import { eventBus } from "@/net/eventBus";
import type { ConnectionState } from "@/net/eventBus";
import { useNetStore } from "@/store/netStore";
import { getWebSocketEndpoints, refreshWebSocketEndpoints } from "@/net/wsEndpoint";

let protocolsRegistered = false;

export function useNet() {
  const { connState, loggedIn, error } = useNetStore();

  useEffect(() => {
    if (!protocolsRegistered) {
      registerHomeProtocols();
      registerGameProtocols();
      protocolsRegistered = true;
    }

    // stateChange 事件类型与 eventBus Events 定义中的 ConnectionState 一致
    const onStateChange = (state: ConnectionState) => {
      useNetStore.getState().setConnState(state);
    };
    const onReconnectCountdown = (seconds: number) => {
      useNetStore.getState().setReconnectCountdown(seconds);
    };
    const onBrowserOnline = () => {
      if (!NetManager.isConnected) NetManager.retryNow(getWebSocketEndpoints());
    };
    const onForegroundResume = () => {
      if (document.visibilityState === "visible") {
        NetManager.handleForegroundResume(getWebSocketEndpoints());
      }
    };
    const onPageShow = () => NetManager.handleForegroundResume(getWebSocketEndpoints());

    eventBus.on("stateChange", onStateChange);
    eventBus.on("reconnectCountdown", onReconnectCountdown);
    window.addEventListener("online", onBrowserOnline);
    window.addEventListener("pageshow", onPageShow);
    document.addEventListener("visibilitychange", onForegroundResume);
    NetManager.connect(getWebSocketEndpoints());
    void refreshWebSocketEndpoints().then((endpoints) => {
      if (["disconnected", "reconnecting", "failed"].includes(NetManager.state)) {
        NetManager.retryNow(endpoints);
      }
    });

    return () => {
      eventBus.off("stateChange", onStateChange);
      eventBus.off("reconnectCountdown", onReconnectCountdown);
      window.removeEventListener("online", onBrowserOnline);
      window.removeEventListener("pageshow", onPageShow);
      document.removeEventListener("visibilitychange", onForegroundResume);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return {
    connState,
    isConnected: connState === "connected",
    isHandshaking: connState === "handshaking",
    loggedIn,
    error,
  };
}
