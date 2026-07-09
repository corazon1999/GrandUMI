"use client";

import { useEffect, useMemo } from "react";
import { useRouter } from "next/navigation";
import { motion, AnimatePresence } from "framer-motion";
import GameBoard from "@/components/game/GameBoard";
import GameMenu from "@/components/game/GameMenu";
import ReconnectOverlay from "@/components/game/ReconnectOverlay";
import OpponentDisconnectBanner from "@/components/game/OpponentDisconnectBanner";
import MulliganOverlay from "@/components/game/MulliganOverlay";
import PromptOverlay from "@/components/game/PromptOverlay";
import PromptSuccessFlash from "@/components/game/PromptSuccessFlash";
import BattleDefenseOverlay from "@/components/game/BattleDefenseOverlay";
import GMPanel from "@/components/game/GMPanel";
import FeedbackOverlay from "@/components/game/FeedbackOverlay";
import { useGameStore } from "@/store/gameStore";
import { useNetStore } from "@/store/netStore";
import { usePlayback } from "@/hooks/usePlayback";
import { useGameInit } from "@/hooks/useGameInit";

export default function GamePage() {
  const router = useRouter();
  const {
    mode,
    isPending,
    isGameOver,
    winnerIsMe,
    gameOverReason,
  } = useGameStore();

  const isObserver = mode === "Observer";
  const isPlayback = mode === "Playback";

  useGameInit();

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

  return (
    <div className="relative h-screen w-screen overflow-hidden bg-[#07111f] text-white select-none">
      {!isObserver && !isPlayback && <ReconnectOverlay />}
      {!isObserver && !isPlayback && <OpponentDisconnectBanner />}
      {!isObserver && !isPlayback && <MulliganOverlay />}
      {!isObserver && !isPlayback && <PromptOverlay />}
      {!isObserver && !isPlayback && <PromptSuccessFlash />}
      {!isObserver && !isPlayback && <BattleDefenseOverlay />}
      {!isObserver && !isPlayback && <GameMenu />}
      {!isObserver && !isPlayback && <GMPanel />}
      {!isPlayback && <FeedbackOverlay />}

      {isObserver && (
        <div className="absolute left-4 top-4 z-20 rounded-full bg-purple-600/80 px-3 py-1 text-xs text-white">
          观战模式
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

      <AnimatePresence>
        {isGameOver && (
          <motion.div
            className="fixed inset-0 z-40 flex flex-col items-center justify-center bg-black/70"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
          >
            <motion.h1
              className={
                winnerIsMe
                  ? "text-5xl font-black text-yellow-400 drop-shadow-[0_0_12px_rgba(250,204,21,0.6)]"
                  : "text-5xl font-black text-gray-400 drop-shadow-[0_0_12px_rgba(156,163,175,0.5)]"
              }
              initial={{ scale: 0.6, opacity: 0 }}
              animate={{ scale: 1, opacity: 1 }}
              transition={{ delay: 0.2, type: "spring", stiffness: 200 }}
            >
              {winnerIsMe ? "你胜利了！" : "你战败了"}
            </motion.h1>
            {gameOverReason && (
              <motion.p
                className="mt-3 text-lg text-white/70"
                initial={{ opacity: 0, y: 8 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.5 }}
              >
                结束原因：{gameOverReason}
              </motion.p>
            )}
            <motion.button
              onClick={() => {
                useGameStore.getState().resetGame();
                useNetStore.getState().setMatchState("idle");
                useNetStore.getState().setOpponentName("");
                router.push("/home");
              }}
              className="mt-6 rounded-lg bg-orange-500 px-6 py-2 text-white transition-colors hover:bg-orange-400"
              initial={{ opacity: 0, y: 12 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.7 }}
            >
              返回大厅
            </motion.button>
          </motion.div>
        )}
      </AnimatePresence>

      <GameBoard isObserver={isObserver} isPlayback={isPlayback} />
    </div>
  );
}
