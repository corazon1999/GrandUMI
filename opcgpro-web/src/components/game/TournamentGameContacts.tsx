"use client";

import type { ReactNode } from "react";
import { useGameStore } from "@/store/gameStore";
import { useTournamentClock } from "@/net/TournamentClient";
import { TournamentContactsContext } from "@/components/ui/TournamentPlayerName";

/** 联系信息只作用于对应赛事牌桌，避免离开赛事后带入其他页面或下一场对局。 */
export default function TournamentGameContacts({ children }: { children: ReactNode }) {
  const kind = useGameStore(state => state.matchKind);
  const roomId = useGameStore(state => state.roomId);
  const clock = useTournamentClock(state => state.clock);
  const contacts = kind === "Tournament" && !!roomId && clock?.roomId === roomId ? clock.contacts ?? [] : null;
  return <TournamentContactsContext.Provider value={contacts}>{children}</TournamentContactsContext.Provider>;
}
