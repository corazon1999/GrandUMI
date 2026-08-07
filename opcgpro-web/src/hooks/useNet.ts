"use client";

import { useEffect } from "react";
import { NetManager } from "@/net/NetManager";
import { registerHomeProtocols } from "@/net/HomeProtocol";
import { registerGameProtocols } from "@/net/GameProtocol";
import { eventBus } from "@/net/eventBus";
import type { ConnectionState } from "@/net/eventBus";
import { useNetStore } from "@/store/netStore";

const WS_URL = process.env.NEXT_PUBLIC_WS_URL ?? "ws://localhost:8080/ws";

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

    eventBus.on("stateChange", onStateChange);
    eventBus.on("reconnectCountdown", onReconnectCountdown);
    NetManager.connect(WS_URL);

    return () => {
      eventBus.off("stateChange", onStateChange);
      eventBus.off("reconnectCountdown", onReconnectCountdown);
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
