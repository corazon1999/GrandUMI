"use client";

import { useEffect, useState } from "react";
import GameBoard from "./GameBoard";
import { createHexLayoutSnapshot } from "./HexActionsLayoutVerification";
import LayoutPreviewRoute from "@/components/home/LayoutPreviewRoute";
import { loadAllCards } from "@/data/CardLoader";
import { useGameStore } from "@/store/gameStore";
import { useTournamentClock, clearTournamentClock } from "@/net/TournamentClient";

/** 使用真实赛事牌桌验证竖屏旋转和独立计时，不创建线上赛事。 */
export default function TournamentGameLayoutVerification() {
  const [ready, setReady] = useState(false);
  useEffect(() => {
    let disposed = false;
    void loadAllCards().then(() => {
      if (disposed) return;
      const state = createHexLayoutSnapshot();
      state.matchKind = "Tournament";
      useGameStore.getState().resetGame();
      useGameStore.getState().syncFromServer(state);
      const now = Date.now();
      useTournamentClock.setState({ offset: 0, clock: { proto: "MsgTournamentClock", tournamentId: "layout", name: "赛事计时验证", round: 1, stage: "swiss", table: 4, startedAt: now, deadline: now + 1_800_000, extraTurnLimit: null, manual: false, serverNow: now } });
      setReady(true);
    });
    return () => { disposed = true; clearTournamentClock(); useGameStore.getState().resetGame(); };
  }, []);
  return <LayoutPreviewRoute><main className="h-full w-full bg-[#07111f]">{ready && <GameBoard isObserver={false} isPlayback={false} />}</main></LayoutPreviewRoute>;
}
