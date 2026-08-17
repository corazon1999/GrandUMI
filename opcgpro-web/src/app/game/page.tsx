"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { motion, AnimatePresence } from "framer-motion";
import GameBoard from "@/components/game/GameBoard";
import GameMenu from "@/components/game/GameMenu";
import ReconnectOverlay from "@/components/game/ReconnectOverlay";
import OpponentDisconnectBanner from "@/components/game/OpponentDisconnectBanner";
import FirstPlayerOverlay from "@/components/game/FirstPlayerOverlay";
import LeaderClashOverlay from "@/components/game/LeaderClashOverlay";
import MulliganOverlay from "@/components/game/MulliganOverlay";
import PromptOverlay from "@/components/game/PromptOverlay";
import PromptSuccessFlash from "@/components/game/PromptSuccessFlash";
import BattleDefenseOverlay from "@/components/game/BattleDefenseOverlay";
import GMPanel from "@/components/game/GMPanel";
import FeedbackOverlay from "@/components/game/FeedbackOverlay";
import AttachDonUndoToast from "@/components/game/AttachDonUndoToast";
import GameOverOverlay from "@/components/game/GameOverOverlay";
import { useGameStore } from "@/store/gameStore";
import { useNetStore } from "@/store/netStore";
import { usePlayback } from "@/hooks/usePlayback";
import { useGameInit } from "@/hooks/useGameInit";
import { HomeRequest } from "@/net/HomeProtocol";
import { GameRequest } from "@/net/GameRequest";

export default function GamePage() {
  const router = useRouter();
  const [leaderClashComplete, setLeaderClashComplete] = useState(false);
  const [feedbackOpenRequest, setFeedbackOpenRequest] = useState(0);
  // 只订阅页面壳实际使用的字段，避免每份完整牌桌快照都让整个页面树重新渲染。
  const mode = useGameStore((s) => s.mode);
  const isPending = useGameStore((s) => s.isPending);
  const isGameOver = useGameStore((s) => s.isGameOver);

  const isObserver = mode === "Observer";
  const isPlayback = mode === "Playback";

  const gameReady = useGameInit();
  const handleLeaderClashComplete = useCallback(() => setLeaderClashComplete(true), []);

  const playbackRecord = useMemo(() => {
    if (!isPlayback) return null;
    try {
      const raw = sessionStorage.getItem("grandumi_playback");
      return raw ? JSON.parse(raw) : null;
    } catch {
      return null;
    }
  }, [isPlayback]);

  const playback = usePlayback(playbackRecord);

  useEffect(() => {
    if (isPlayback && playback.state === "idle") {
      playback.play();
    }
  }, [isPlayback, playback]);

  useEffect(() => () => {
    GameRequest.cancelPendingAttachDon();
  }, []);

  const returnToHome = () => {
    GameRequest.leaveGameChat();
    if (isObserver) HomeRequest.leaveSpectate();
    useGameStore.getState().resetGame();
    useGameStore.getState().setMode("Player");
    useNetStore.getState().setMatchState("idle");
    useNetStore.getState().setOpponentName("");
    router.push("/home");
  };

  return (
    <div className="relative h-full w-full overflow-hidden bg-[#07111f] text-white select-none">
      {!isObserver && !isPlayback && <ReconnectOverlay />}
      {!isObserver && !isPlayback && <OpponentDisconnectBanner />}
      {!isObserver && !isPlayback && (
        <>
          <LeaderClashOverlay ready={gameReady} onComplete={handleLeaderClashComplete} />
          {leaderClashComplete && <FirstPlayerOverlay />}
        </>
      )}
      {!isObserver && !isPlayback && leaderClashComplete && <MulliganOverlay />}
      {!isObserver && !isPlayback && <PromptOverlay />}
      {!isObserver && !isPlayback && <PromptSuccessFlash />}
      {!isObserver && !isPlayback && <BattleDefenseOverlay />}
      {!isObserver && !isPlayback && <GameMenu />}
      {!isObserver && !isPlayback && <GMPanel />}
      {!isPlayback && <FeedbackOverlay context="game" openRequest={feedbackOpenRequest} />}
      {!isObserver && !isPlayback && <AttachDonUndoToast />}

      {isObserver && (
        <div
          className="absolute z-[90] flex items-center gap-2"
          style={{
            left: "calc(1rem + var(--layout-safe-left, env(safe-area-inset-left)))",
            top: "calc(1rem + var(--layout-safe-top, env(safe-area-inset-top)))",
          }}
        >
          <div className="rounded-full bg-purple-600/80 px-3 py-1 text-xs text-white">
            观战模式
          </div>
          {!isGameOver && (
            <button
              onClick={returnToHome}
              className="min-h-12 rounded-lg border border-white/20 bg-gray-950/80 px-4 py-2 text-xs font-bold text-white transition-colors hover:bg-gray-800"
            >
              退出观战
            </button>
          )}
        </div>
      )}

      {isPlayback && (
        <div className="absolute left-4 top-4 z-20 rounded-full bg-green-600/80 px-3 py-1 text-xs text-white">
          回放模式
        </div>
      )}

      {!isObserver && !isPlayback && (
        <AnimatePresence>
          {isPending && (
            <motion.div
              className="fixed inset-0 z-30 flex cursor-wait items-center justify-center bg-black/30 pointer-events-auto"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
            >
              <div className="h-8 w-8 animate-spin rounded-full border-3 border-white/40 border-t-white" />
            </motion.div>
          )}
        </AnimatePresence>
      )}

      <GameOverOverlay isObserver={isObserver} onReturnToHome={returnToHome} />

      <GameBoard
        isObserver={isObserver}
        isPlayback={isPlayback}
        onOpenFeedback={
          !isObserver && !isPlayback
            ? () => setFeedbackOpenRequest((request) => request + 1)
            : undefined
        }
      />
    </div>
  );
}
