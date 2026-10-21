import { create } from "zustand";
import { NetManager } from "./NetManager";
import { eventBus } from "./eventBus";
import type { MsgBase } from "@/types/net";
import type { TournamentClock, TournamentResponse } from "@/types/tournament";

export const useTournamentClock = create<{ clock: TournamentClock | null; offset: number }>(() => ({ clock: null, offset: 0 }));
let registered = false;
export function registerTournamentProtocols() {
  if (registered) return;
  registered = true;
  eventBus.on("message", (message) => {
    if (message.proto !== "MsgTournamentClock") return;
    const clock = message as TournamentClock;
    useTournamentClock.setState({ clock, offset: clock.serverNow - Date.now() });
  });
  eventBus.on("sessionReplaced", clearTournamentClock);
}
export function clearTournamentClock() { useTournamentClock.setState({ clock: null, offset: 0 }); }

export function requestTournament(action: string, values: Record<string, unknown> = {}): Promise<TournamentResponse> {
  const requestId = `tournament-${globalThis.crypto.randomUUID()}`;
  return new Promise((resolve, reject) => {
    const cleanup = () => { clearTimeout(timeout); eventBus.off("message", receive); };
    const receive = (message: MsgBase) => {
      if (message.proto !== "MsgTournament" || (message as TournamentResponse).requestId !== requestId) return;
      cleanup();
      const response = message as TournamentResponse;
      if (!response.result) reject(new Error(response.logStr || "赛事操作失败，请刷新重试。"));
      else resolve(response);
    };
    const timeout = setTimeout(() => { cleanup(); reject(new Error("赛事请求超时，请刷新查看是否已经生效。")); }, 20_000);
    eventBus.on("message", receive);
    const message = { proto: "MsgTournament", action, ...values, requestId };
    if (!NetManager.send(message)) {
      cleanup(); reject(new Error("网络未连接，请连接后重试。"));
    }
  });
}
