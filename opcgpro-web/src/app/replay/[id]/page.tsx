"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import GameBoard from "@/components/game/GameBoard";
import PlaybackControls from "@/components/game/PlaybackControls";
import { useGameStore } from "@/store/gameStore";
import { useGameInit } from "@/hooks/useGameInit";
import { getSnapshots } from "@/data/matchHistoryDB";
import type { MsgGameState } from "@/types/net";
import type { PlaybackSpeed } from "@/types/playback";

// 单帧基准时长（1x），按倍速缩放
const STEP_MS = 900;

/**
 * 回放页（路线二）
 *
 * 从浏览器本地 IndexedDB 读取该局的 MsgGameState 快照流，
 * 逐帧调用 gameStore.syncFromServer 驱动共享的 GameBoard 渲染，
 * 配合 PlaybackControls 做播放/暂停/步进/倍速。
 */
export default function ReplayPage() {
  const params = useParams();
  const router = useRouter();
  const id = typeof params?.id === "string" ? params.id : Array.isArray(params?.id) ? params.id[0] : "";

  const [snapshots, setSnapshots] = useState<MsgGameState[] | null>(null);
  const [idx, setIdx] = useState(0);
  const [playing, setPlaying] = useState(false);
  const [speed, setSpeed] = useState<PlaybackSpeed>(1);

  // 进入回放模式驱动 GameBoard 的只读分支；卸载时复位，避免污染后续真实对局
  useGameInit();

  useEffect(() => {
    // 进入前清空上一局残留状态（含操作日志），再切回放模式
    useGameStore.getState().resetGame();
    useGameStore.getState().setMode("Playback");
    return () => {
      useGameStore.getState().resetGame();
      useGameStore.getState().setMode("Player");
    };
  }, []);

  // 加载快照
  useEffect(() => {
    if (!id) return;
    let alive = true;
    getSnapshots(decodeURIComponent(id))
      .then((s) => {
        if (!alive) return;
        setSnapshots(s ?? []);
        if (s && s.length > 0) {
          useGameStore.getState().syncFromServer(s[0]);
          setPlaying(true);
        }
      })
      .catch(() => alive && setSnapshots([]));
    return () => {
      alive = false;
    };
  }, [id]);

  const total = snapshots?.length ?? 0;
  const lastIdx = Math.max(0, total - 1);
  const isEnded = total > 0 && idx >= lastIdx;

  // 应用当前帧
  useEffect(() => {
    if (!snapshots || snapshots.length === 0) return;
    const clamped = Math.min(idx, snapshots.length - 1);
    useGameStore.getState().syncFromServer(snapshots[clamped]);
  }, [idx, snapshots]);

  // 自动播放定时器
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(() => {
    if (!playing || !snapshots || snapshots.length === 0) return;
    if (idx >= snapshots.length - 1) {
      setPlaying(false);
      return;
    }
    timerRef.current = setTimeout(() => setIdx((i) => i + 1), STEP_MS / speed);
    return () => {
      if (timerRef.current) clearTimeout(timerRef.current);
    };
  }, [playing, idx, speed, snapshots]);

  const totalTurns = useMemo(() => {
    if (!snapshots || snapshots.length === 0) return 0;
    return snapshots.reduce((m, s) => Math.max(m, s.turnCount ?? 0), 0);
  }, [snapshots]);
  const currentTurn = snapshots?.[Math.min(idx, lastIdx)]?.turnCount ?? 0;

  const handlePlay = () => {
    if (isEnded) {
      setIdx(0);
      setPlaying(true);
    } else {
      setPlaying(true);
    }
  };

  if (!id) {
    return <div className="flex h-screen items-center justify-center bg-[#07111f] text-white">无效的回放 ID</div>;
  }
  if (snapshots === null) {
    return <div className="flex h-screen items-center justify-center bg-[#07111f] text-white">加载中…</div>;
  }
  if (snapshots.length === 0) {
    return (
      <div className="flex h-screen flex-col items-center justify-center gap-4 bg-[#07111f] text-white">
        <p>未找到该回放数据（可能已被清理或在其它设备录制）</p>
        <button
          onClick={() => router.push("/home")}
          className="rounded-lg bg-orange-500 px-5 py-2 transition-colors hover:bg-orange-400"
        >
          返回大厅
        </button>
      </div>
    );
  }

  return (
    <div className="relative h-screen w-screen overflow-hidden bg-[#07111f] text-white select-none">
      {/* 顶部返回与标识 */}
      <button
        onClick={() => router.push("/home")}
        className="absolute left-4 top-4 z-30 rounded-full bg-gray-800/80 px-3 py-1 text-xs text-white backdrop-blur-sm transition-colors hover:bg-gray-700"
      >
        ← 返回大厅
      </button>
      <div className="absolute left-1/2 top-4 z-20 -translate-x-1/2 rounded-full bg-green-600/80 px-3 py-1 text-xs text-white">
        回放模式
      </div>

      <GameBoard isObserver={false} isPlayback />

      <PlaybackControls
        currentTurn={currentTurn}
        currentStep={idx}
        totalTurns={totalTurns}
        totalSteps={lastIdx}
        isPlaying={playing}
        isEnded={isEnded}
        speed={speed}
        onPlay={handlePlay}
        onPause={() => setPlaying(false)}
        onStepForward={() => setIdx((i) => Math.min(lastIdx, i + 1))}
        onStepBackward={() => setIdx((i) => Math.max(0, i - 1))}
        onSpeedChange={setSpeed}
      />
    </div>
  );
}
