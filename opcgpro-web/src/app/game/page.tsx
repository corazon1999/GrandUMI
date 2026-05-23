"use client";

import { useEffect, useMemo } from "react";
import { useRouter } from "next/navigation";
import { motion, AnimatePresence } from "framer-motion";
import HandArea from "@/components/game/HandArea";
import FieldArea from "@/components/game/FieldArea";
import LeaderCard from "@/components/game/LeaderCard";
import LifeArea from "@/components/game/LifeArea";
import CostArea from "@/components/game/CostArea";
import DonArea from "@/components/game/DonArea";
import GameMenu from "@/components/game/GameMenu";
import ReconnectOverlay from "@/components/game/ReconnectOverlay";
import OpponentDisconnectBanner from "@/components/game/OpponentDisconnectBanner";
import AnimationLayer from "@/components/game/AnimationLayer";
import MulliganOverlay from "@/components/game/MulliganOverlay";
import PromptOverlay from "@/components/game/PromptOverlay";
import PlaybackControls from "@/components/game/PlaybackControls";
import { useGameStore } from "@/store/gameStore";
import { usePlayback } from "@/hooks/usePlayback";
import { useGameInit } from "@/hooks/useGameInit";
import { PHASE_LABELS } from "@/game/battle/BattlePhase";

export default function GamePage() {
  const router = useRouter();
  const {
    mode, currentTurn, phase, myName, opponentName,
    isPending, isGameOver,
  } = useGameStore();

  const isObserver = mode === "Observer";
  const isPlayback = mode === "Playback";

  useGameInit();

  // ── 回放模式：从 sessionStorage 加载回放记录 ──
  const playbackRecord = useMemo(() => {
    if (!isPlayback) return null;
    try {
      const raw = sessionStorage.getItem("grandumi_playback");
      return raw ? JSON.parse(raw) : null;
    } catch { return null; }
  }, [isPlayback]);

  const playback = usePlayback(playbackRecord);

  // 回放模式下自动开始
  useEffect(() => {
    if (isPlayback && playback.state === "idle") {
      playback.play();
    }
  }, [isPlayback, playback]);

  return (
    <div className="relative w-screen h-screen bg-gray-900 overflow-hidden select-none">
      {/* 断线重连遮罩（观战/回放模式下不显示） */}
      {!isObserver && !isPlayback && <ReconnectOverlay />}

      {/* 对手断线横幅 */}
      {!isObserver && !isPlayback && <OpponentDisconnectBanner />}

      {/* 战斗动画特效层 */}
      <AnimationLayer />

      {/* 换牌阶段遮罩（仅 Player 模式） */}
      {!isObserver && !isPlayback && <MulliganOverlay />}

      {/* 服务端 Prompt 遮罩（选目标 / 选项 / 生命牌触发） */}
      {!isObserver && !isPlayback && <PromptOverlay />}

      {/* 观战模式标识 */}
      {isObserver && (
        <div className="absolute top-3 left-3 z-20 bg-purple-600/80 text-white text-xs px-3 py-1 rounded-full">
          观战模式
        </div>
      )}

      {/* 回放模式标识 */}
      {isPlayback && (
        <div className="absolute top-3 left-3 z-20 bg-green-600/80 text-white text-xs px-3 py-1 rounded-full">
          回放模式
        </div>
      )}

      {/* 请求锁遮罩（仅 Player 模式） */}
      {!isObserver && !isPlayback && (
        <AnimatePresence>
          {isPending && (
            <motion.div
              className="fixed inset-0 z-30 bg-black/30 flex items-center justify-center pointer-events-auto cursor-wait"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
            >
              <div className="w-8 h-8 border-3 border-white/40 border-t-white rounded-full animate-spin" />
            </motion.div>
          )}
        </AnimatePresence>
      )}

      {/* 游戏结束遮罩 */}
      <AnimatePresence>
        {isGameOver && (
          <motion.div
            className="fixed inset-0 z-40 bg-black/70 flex flex-col items-center justify-center"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
          >
            <p className="text-white text-2xl font-bold mb-4">游戏结束</p>
            <button
              onClick={() => {
                useGameStore.getState().resetGame();
                router.push("/home");
              }}
              className="bg-orange-500 text-white px-6 py-2 rounded-lg hover:bg-orange-400 transition-colors"
            >
              返回大厅
            </button>
          </motion.div>
        )}
      </AnimatePresence>

      {/* 对手区域（上半部分） */}
      <div className="absolute top-0 left-0 right-0 h-[48%] flex flex-col-reverse pb-2">
        <FieldArea side="opponent" />
        {/* 观战模式下双方手牌均隐藏 */}
        <HandArea side="opponent" hidden />
      </div>

      {/* 中线信息栏 */}
      <div className="absolute top-1/2 left-16 right-16 sm:left-14 sm:right-14 -translate-y-1/2 flex justify-between items-center px-4 py-1 bg-black/50 rounded-full">
        <div className="flex items-center gap-2">
          <CostArea side="opponent" />
          <span className="text-gray-300 text-xs font-medium truncate max-w-[80px]">
            {opponentName || "对手"}
          </span>
        </div>
        <span className="text-white text-sm font-bold tracking-wide">
          {isObserver
            ? `回合 ${Math.ceil((useGameStore.getState().turnCount || 1) / 2)}`
            : currentTurn
              ? "我的回合"
              : "对手回合"}
          {" · "}{PHASE_LABELS[phase]}
        </span>
        <div className="flex items-center gap-2">
          <span className="text-orange-300 text-xs font-medium truncate max-w-[80px]">
            {myName || "我"}
          </span>
          <CostArea side="my" />
        </div>
      </div>

      {/* 我方区域（下半部分） */}
      <div className="absolute bottom-0 left-0 right-0 h-[48%] flex flex-col pt-2">
        <FieldArea side="my" />
        {/* 咚!!区域（仅己方） */}
        {!isObserver && !isPlayback && <DonArea side="my" />}
        {/* 观战模式下双方手牌均隐藏 */}
        <HandArea side="my" hidden={isObserver} />
      </div>

      {/* 领航卡（左侧） */}
      <div className="absolute left-2 top-1/2 -translate-y-1/2 flex flex-col gap-3">
        <LeaderCard side="opponent" />
        <LeaderCard side="my" />
      </div>

      {/* 生命值（右侧） */}
      <div className="absolute right-2 top-1/2 -translate-y-1/2 flex flex-col gap-3">
        <LifeArea side="opponent" />
        <LifeArea side="my" />
      </div>

      {/* 游戏菜单（仅 Player 模式） */}
      {!isObserver && !isPlayback && <GameMenu />}

      {/* 回放控件（M6 真正接入） */}
      {/* {isPlayback && playback && <PlaybackControls ... />} */}
    </div>
  );
}
