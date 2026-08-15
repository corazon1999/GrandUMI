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
import RankResultPanel from "@/components/game/RankResultPanel";
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
  const isDraw = useGameStore((s) => s.isDraw);
  const winnerIsMe = useGameStore((s) => s.winnerIsMe);
  const gameOverReason = useGameStore((s) => s.gameOverReason);
  const matchKind = useGameStore((s) => s.matchKind);
  const rankResult = useNetStore((s) => s.lastRankResult);

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

      <AnimatePresence>
        {isGameOver && (
          <motion.div
            className="fixed inset-0 z-40 flex flex-col items-center overflow-y-auto bg-black/70 px-[calc(1rem+var(--layout-safe-left,env(safe-area-inset-left)))] py-[calc(1rem+var(--layout-safe-top,env(safe-area-inset-top)))]"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
          >
            <motion.h1
              className={
                isObserver
                  ? "text-5xl font-black text-purple-300 drop-shadow-[0_0_12px_rgba(216,180,254,0.5)]"
                  : isDraw
                  ? "text-5xl font-black text-sky-300 drop-shadow-[0_0_12px_rgba(125,211,252,0.5)]"
                  : winnerIsMe
                  ? "text-5xl font-black text-yellow-400 drop-shadow-[0_0_12px_rgba(250,204,21,0.6)]"
                  : "text-5xl font-black text-gray-400 drop-shadow-[0_0_12px_rgba(156,163,175,0.5)]"
              }
              initial={{ scale: 0.6, opacity: 0 }}
              animate={{ scale: 1, opacity: 1 }}
              transition={{ delay: 0.2, type: "spring", stiffness: 200 }}
            >
              {isObserver ? "对局结束" : isDraw ? "本局平局" : winnerIsMe ? "你胜利了！" : "你战败了"}
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
            {!isDraw && matchKind === "Ranked" && rankResult && (
              <motion.div
                className="contents"
                initial={{ opacity: 0, y: 8 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.6 }}
              >
                <RankResultPanel result={rankResult} />
              </motion.div>
            )}
            <motion.button
              onClick={returnToHome}
              className="mt-4 min-h-11 rounded-lg bg-orange-500 px-6 py-2 text-white transition-colors hover:bg-orange-400"
              initial={{ opacity: 0, y: 12 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.7 }}
            >
              返回大厅
            </motion.button>
          </motion.div>
        )}
      </AnimatePresence>

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
