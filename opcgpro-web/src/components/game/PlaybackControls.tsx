"use client";

import { motion, AnimatePresence } from "framer-motion";
import { useState } from "react";
import type { PlaybackSpeed } from "@/types/playback";

interface Props {
  currentTurn: number;
  currentStep: number;
  totalTurns: number;
  totalSteps: number;
  isPlaying: boolean;
  isEnded: boolean;
  speed: PlaybackSpeed;
  onPlay: () => void;
  onPause: () => void;
  onStepForward: () => void;
  onStepBackward: () => void;
  onSpeedChange: (speed: PlaybackSpeed) => void;
}

const SPEEDS: PlaybackSpeed[] = [0.5, 1, 2, 4];
const SPEED_LABELS: Record<PlaybackSpeed, string> = { 0.5: "0.5x", 1: "1x", 2: "2x", 4: "4x" };

export default function PlaybackControls({
  currentTurn,
  currentStep,
  totalTurns,
  totalSteps,
  isPlaying,
  isEnded,
  speed,
  onPlay,
  onPause,
  onStepForward,
  onStepBackward,
  onSpeedChange,
}: Props) {
  const [collapsed, setCollapsed] = useState(false);

  return (
    <motion.div
      className="fixed bottom-4 left-1/2 -translate-x-1/2 z-50"
      initial={{ opacity: 0, y: 30 }}
      animate={{ opacity: 1, y: 0 }}
    >
      {/* 折叠/展开切换 */}
      <button
        onClick={() => setCollapsed(!collapsed)}
        className="absolute -top-3 left-1/2 -translate-x-1/2 w-6 h-3 bg-gray-800 rounded-t-md flex items-center justify-center"
      >
        <span className="text-gray-500 text-[8px]">{collapsed ? "▲" : "▼"}</span>
      </button>

      <AnimatePresence>
        {!collapsed && (
          <motion.div
            className="bg-gray-900/95 backdrop-blur-sm border border-gray-700 rounded-xl px-4 py-3 flex flex-col gap-2 shadow-2xl min-w-[320px]"
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: "auto", opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.15 }}
          >
            {/* 进度信息 */}
            <div className="flex items-center justify-between text-xs text-gray-400">
              <span>
                回合 {currentTurn + 1}/{totalTurns}
              </span>
              <span>
                步骤 {currentStep}/{totalSteps}
              </span>
            </div>

            {/* 进度条 */}
            <div className="h-1 bg-gray-800 rounded-full overflow-hidden">
              <motion.div
                className="h-full bg-green-500 rounded-full"
                animate={{
                  width: totalSteps > 0
                    ? `${(currentStep / totalSteps) * 100}%`
                    : "0%",
                }}
                transition={{ duration: 0.2 }}
              />
            </div>

            {/* 控制按钮 */}
            <div className="flex items-center justify-center gap-2">
              {/* 上一步 */}
              <button
                onClick={onStepBackward}
                disabled={currentStep <= 0}
                className="w-8 h-8 flex items-center justify-center rounded-lg bg-gray-800 hover:bg-gray-700 disabled:opacity-30 disabled:cursor-not-allowed text-white transition-colors"
                title="上一步"
              >
                ⏮
              </button>

              {/* 播放/暂停 */}
              {isEnded ? (
                <button
                  onClick={onPlay}
                  className="w-10 h-10 flex items-center justify-center rounded-full bg-green-600 hover:bg-green-500 text-white transition-colors"
                  title="重新播放"
                >
                  ↺
                </button>
              ) : isPlaying ? (
                <button
                  onClick={onPause}
                  className="w-10 h-10 flex items-center justify-center rounded-full bg-yellow-600 hover:bg-yellow-500 text-white transition-colors"
                  title="暂停"
                >
                  ⏸
                </button>
              ) : (
                <button
                  onClick={onPlay}
                  className="w-10 h-10 flex items-center justify-center rounded-full bg-green-600 hover:bg-green-500 text-white transition-colors"
                  title="播放"
                >
                  ▶
                </button>
              )}

              {/* 下一步 */}
              <button
                onClick={onStepForward}
                disabled={isEnded}
                className="w-8 h-8 flex items-center justify-center rounded-lg bg-gray-800 hover:bg-gray-700 disabled:opacity-30 disabled:cursor-not-allowed text-white transition-colors"
                title="下一步"
              >
                ⏭
              </button>
            </div>

            {/* 速度选择 */}
            <div className="flex items-center justify-center gap-1">
              {SPEEDS.map((s) => (
                <button
                  key={s}
                  onClick={() => onSpeedChange(s)}
                  className={`px-2 py-0.5 rounded text-xs font-medium transition-colors ${
                    speed === s
                      ? "bg-green-600 text-white"
                      : "bg-gray-800 text-gray-400 hover:bg-gray-700 hover:text-white"
                  }`}
                >
                  {SPEED_LABELS[s]}
                </button>
              ))}
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </motion.div>
  );
}
