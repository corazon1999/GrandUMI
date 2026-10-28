"use client";

import { useEffect, useState } from "react";
import GameBoard from "./GameBoard";
import { createHexLayoutSnapshot } from "./HexActionsLayoutVerification";
import LayoutPreviewRoute from "@/components/home/LayoutPreviewRoute";
import { loadAllCards } from "@/data/CardLoader";
import { useGameStore } from "@/store/gameStore";
import { useTournamentClock, clearTournamentClock } from "@/net/TournamentClient";
import { useSettingsStore } from "@/store/settingsStore";

/** 使用真实赛事牌桌验证竖屏旋转和独立计时，不创建线上赛事。 */
export default function TournamentGameLayoutVerification({ timing }: { timing?: "overdue" | "final" }) {
  const [ready, setReady] = useState(false);
  useEffect(() => {
    let disposed = false;
    const previousSettings = useSettingsStore.getState();
    useSettingsStore.setState({ animationSpeed: "off", visualEffectsEnabled: false });
    void loadAllCards().then(() => {
      if (disposed) return;
      const state = createHexLayoutSnapshot();
      state.matchKind = "Tournament";
      state.roomId = "layout-tournament-room";
      state.my.name = "赛事选手甲"; state.opponent.name = "赛事选手乙";
      useGameStore.getState().resetGame();
      useGameStore.getState().syncFromServer(state);
      const now = Date.now();
      useTournamentClock.setState({ offset: 0, clock: { proto: "MsgTournamentClock", tournamentId: "layout", name: "赛事计时验证", round: timing === "final" ? 5 : 1, stage: timing === "final" ? "cut" : "swiss", isFinal: timing === "final", table: 4, startedAt: now, deadline: timing === "final" ? null : timing === "overdue" ? now - 60_000 : now + 1_800_000, extraTurnLimit: null, manual: false, serverNow: now, roomId: state.roomId,
        contacts: [{ account: "p0", name: state.my.name, qq: "1000000000" }, { account: "p1", name: state.opponent.name, qq: "1000000001" }] } });
      setReady(true);
    });
    return () => { disposed = true; clearTournamentClock(); useGameStore.getState().resetGame(); useSettingsStore.setState({ animationSpeed: previousSettings.animationSpeed, visualEffectsEnabled: previousSettings.visualEffectsEnabled }); };
  }, [timing]);
  return <LayoutPreviewRoute><main className="h-full w-full bg-[#07111f]">{ready && <GameBoard isObserver={false} isPlayback={false} />}</main></LayoutPreviewRoute>;
}
