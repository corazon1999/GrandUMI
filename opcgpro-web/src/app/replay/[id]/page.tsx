"use client";

import { useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { useGameStore } from "@/store/gameStore";

/**
 * 回放页面（M6）
 *
 * 通过 fetch 拉取服务端 Replays/{date}/{id}.jsonl
 * 逐 tick 应用 MsgGameState 类型行，按时间轴播放。
 *
 * 当前 M6 仅提供数据流框架；UI 播放控件复用 PlaybackControls。
 */
export default function ReplayPage() {
  const params = useParams();
  const id = params?.id;
  const [steps, setSteps] = useState<unknown[]>([]);
  const [idx, setIdx] = useState(0);

  useEffect(() => {
    if (!id) return;
    fetch(`/api/replay/${id}`)
      .then((r) => r.ok ? r.text() : Promise.reject(new Error("not found")))
      .then((text) => {
        const lines = text.split("\n").filter(Boolean);
        const items = lines.map((l) => { try { return JSON.parse(l); } catch { return null; } }).filter(Boolean);
        setSteps(items as unknown[]);
      })
      .catch(() => setSteps([]));
  }, [id]);

  useEffect(() => {
    if (idx >= steps.length) return;
    const step = steps[idx] as { kind: string; snapshot?: object };
    if (step.kind === "state" && step.snapshot) {
      useGameStore.getState().syncFromServer(step.snapshot as never);
    }
  }, [idx, steps]);

  if (!id) return <div className="text-white">无效的回放 ID</div>;
  if (steps.length === 0) return <div className="text-white p-6">加载中…</div>;

  return (
    <div className="fixed bottom-4 left-1/2 -translate-x-1/2 bg-gray-900 text-white px-4 py-2 rounded-xl flex items-center gap-3 z-50">
      <button onClick={() => setIdx(Math.max(0, idx - 1))} className="px-2">⏮</button>
      <span>{idx + 1} / {steps.length}</span>
      <button onClick={() => setIdx(Math.min(steps.length - 1, idx + 1))} className="px-2">⏭</button>
    </div>
  );
}
