"use client";

import { useEffect, useState } from "react";
import { useNetStore } from "@/store/netStore";
import FriendlyRoomPanel from "./FriendlyRoomPanel";
import LayoutPreviewFrame from "./LayoutPreviewFrame";
import LobbyPanel from "./LobbyPanel";

export default function FriendlyHexLayoutVerification({ view }: { view: "lobby" | "room" }) {
  const [ready, setReady] = useState(false);

  useEffect(() => {
    useNetStore.setState({
      account: "layout-host",
      playerName: "布局验证房主",
      connState: "disconnected",
      matchState: "idle",
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
