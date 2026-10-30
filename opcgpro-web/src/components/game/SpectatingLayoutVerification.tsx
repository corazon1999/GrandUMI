"use client";

import { useEffect, useState } from "react";
import LayoutPreviewRoute from "@/components/home/LayoutPreviewRoute";
import SpectateJoinButton from "@/components/home/SpectateJoinButton";
import { HomeRequest } from "@/net/HomeProtocol";
import { useNetStore } from "@/store/netStore";
import { useGameStore } from "@/store/gameStore";
import GameChatPanel from "./GameChatPanel";

/** 使用真实观战入口、权限状态和旋转聊天控件验证管理员观战。 */
export default function SpectatingLayoutVerification({ game, administrator }: { game: boolean; administrator: boolean }) {
  const [ready, setReady] = useState(false);
  const [entered, setEntered] = useState("");
  useEffect(() => {
    const previous = useNetStore.getState();
    const spectateRoom = HomeRequest.spectateRoom;
    HomeRequest.spectateRoom = (roomId) => { setEntered(roomId); return true; };
    useNetStore.setState({ maintenance: { ...previous.maintenance, canManage: administrator }, spectateState: "idle" });
    useGameStore.setState({
      spectatorNames: ["管理员观众", "普通观众"],
      spectatorDetails: [
        { account: "释迦", name: "管理员观众", viewingYou: true, handVisible: false, canKick: false },
        { account: "普通观众", name: "普通观众", viewingYou: false, handVisible: false, canKick: true },
      ],
    });
    setReady(true);
    return () => {
      HomeRequest.spectateRoom = spectateRoom;
      useNetStore.setState({ maintenance: previous.maintenance, spectateState: previous.spectateState });
      useGameStore.getState().resetGame();
    };
  }, [administrator]);

  if (!ready) return null;
  if (game) return <LayoutPreviewRoute><main className="relative h-full w-full bg-[#07111f]">
    <GameChatPanel isObserver={false} isPlayback={false} />
  </main></LayoutPreviewRoute>;
  return <main className="flex min-h-dvh flex-col items-start gap-4 bg-slate-950 p-6 text-white">
    {(["open", "closed", "friends", "password"] as const).map((mode) => <div key={mode} data-spectate-mode={mode}>
      <SpectateJoinButton roomId={mode} seatIndex={0} mode={mode} />
    </div>)}
    <output data-spectate-entered>{entered}</output>
  </main>;
}
