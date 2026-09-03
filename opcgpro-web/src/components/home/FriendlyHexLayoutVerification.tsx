"use client";

import { useEffect, useState } from "react";
import { useNetStore } from "@/store/netStore";
import FriendlyRoomPanel from "./FriendlyRoomPanel";
import LayoutPreviewFrame from "./LayoutPreviewFrame";
import LobbyPanel from "./LobbyPanel";

export default function FriendlyHexLayoutVerification({ view }: { view: "lobby" | "room" | "ranked" }) {
  const [ready, setReady] = useState(false);

  useEffect(() => {
    const rankedProfile = view === "ranked"
      ? {
          seasonId: "layout-ranked-season",
          seasonStartsAtUtc: "2026-09-01T00:00:00Z",
          seasonEndsAtUtc: "2026-10-27T00:00:00Z",
          placementGames: 5,
          placementRequired: 5,
          rankPoints: 20_000,
          faction: "pirate" as const,
          tier: "超新星",
          division: null,
          games: 24,
          wins: 15,
          losses: 9,
          highestRankPoints: 20_000,
          championLeaderNumbers: [],
        }
      : null;
    useNetStore.setState({
      account: "layout-host",
      playerName: "布局验证房主",
      connState: "disconnected",
      matchState: "idle",
      matchQueueKind: view === "ranked" ? "ranked" : "casualStandard",
      rankProfile: rankedProfile,
      rankProfiles: { standard: rankedProfile, wild: null },
      roomCode: null,
      roomOperation: "idle",
      selectedDeck: {
        name: "海克斯好友房验证卡组",
        leader: "OP15-001",
        leaderName: "布局验证领航",
        cards: "OP15-001",
      },
      maintenance: { enabled: false, activeRoomCount: 0, startedAt: null, canManage: false },
      friendlyRoom: view === "room"
        ? {
            roomId: "layout-friendly-hex",
            origin: "roomCode",
            roomCode: "HEX903",
            hexMode: true,
            players: [
              {
                account: "layout-host",
                name: "布局验证房主",
                deckName: "海克斯好友房验证卡组",
                ready: false,
                connected: true,
              },
            ],
            scores: [0, 0],
            state: "lobby",
          }
        : null,
    });
    setReady(true);
  }, [view]);

  return (
    <LayoutPreviewFrame mode="desktop">
      <div data-friendly-hex-layout-verification={view} className="h-full min-h-0 overflow-hidden bg-gray-950">
        {ready && (view === "room" ? <FriendlyRoomPanel /> : <LobbyPanel onGoToDeck={() => undefined} />)}
      </div>
    </LayoutPreviewFrame>
  );
}
