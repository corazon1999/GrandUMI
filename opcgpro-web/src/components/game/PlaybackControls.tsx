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
      className="fixed left-1/2 z-50 -translate-x-1/2"
      style={{ bottom: "calc(1rem + var(--layout-safe-bottom, 0px))" }}
      initial={{ opacity: 0, y: 30 }}
      animate={{ opacity: 1, y: 0 }}
    >
      {/* 折叠/展开切换 */}
      <button
        onClick={() => setCollapsed(!collapsed)}
        className="absolute -top-12 left-1/2 flex h-12 w-12 -translate-x-1/2 items-end justify-center rounded-t-md bg-transparent pb-1"
        aria-label={collapsed ? "展开回放控件" : "收起回放控件"}
      >
        <span className="flex h-3 w-6 items-center justify-center rounded-t-md bg-gray-800 text-[8px] text-gray-500">
          {collapsed ? "▲" : "▼"}
        </span>
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
                className="flex h-12 w-12 items-center justify-center rounded-lg bg-gray-800 text-white transition-colors hover:bg-gray-700 disabled:cursor-not-allowed disabled:opacity-30"
                title="上一步"
              >
                ⏮
              </button>

              {/* 播放/暂停 */}
              {isEnded ? (
                <button
                  onClick={onPlay}
                  className="flex h-12 w-12 items-center justify-center rounded-full bg-green-600 text-white transition-colors hover:bg-green-500"
                  title="重新播放"
                >
                  ↺
                </button>
              ) : isPlaying ? (
                <button
                  onClick={onPause}
                  className="flex h-12 w-12 items-center justify-center rounded-full bg-yellow-600 text-white transition-colors hover:bg-yellow-500"
                  title="暂停"
                >
                  ⏸
                </button>
              ) : (
                <button
                  onClick={onPlay}
                  className="flex h-12 w-12 items-center justify-center rounded-full bg-green-600 text-white transition-colors hover:bg-green-500"
                  title="播放"
                >
                  ▶
                </button>
              )}

              {/* 下一步 */}
              <button
                onClick={onStepForward}
                disabled={isEnded}
                className="flex h-12 w-12 items-center justify-center rounded-lg bg-gray-800 text-white transition-colors hover:bg-gray-700 disabled:cursor-not-allowed disabled:opacity-30"
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
                  className={`flex h-12 min-w-12 items-center justify-center rounded px-2 text-xs font-medium transition-colors ${
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
