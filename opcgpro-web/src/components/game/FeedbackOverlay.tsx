"use client";

/**
 * FeedbackOverlay — 游戏内 Bug 反馈
 *
 * 仅在 game 页挂载，按 F2 弹出/关闭。输入问题描述后提交，
 * 连同客户端全量信息(gameStore 镜像 + 元信息)发送给服务端落盘。
 */

import { useEffect, useRef, useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { NetManager } from "@/net/NetManager";
import { eventBus } from "@/net/eventBus";
import { useGameStore } from "@/store/gameStore";
import type { MsgBase, MsgBugReport } from "@/types/net";

type SubmitState =
  | { kind: "idle" }
  | { kind: "sending" }
  | { kind: "ok"; path?: string }
  | { kind: "fail"; error?: string };

export default function FeedbackOverlay() {
  const [open, setOpen] = useState(false);
  const [description, setDescription] = useState("");
  const [submit, setSubmit] = useState<SubmitState>({ kind: "idle" });
  const textRef = useRef<HTMLTextAreaElement>(null);

  // F2 切换显隐
  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.key !== "F2") return;
      e.preventDefault();
      setOpen((v) => !v);
    }
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  // 打开时聚焦、重置提交状态
  useEffect(() => {
    if (open) {
      setSubmit({ kind: "idle" });
      // 等待动画后聚焦
      const t = setTimeout(() => textRef.current?.focus(), 50);
      return () => clearTimeout(t);
    }
  }, [open]);

  // 订阅服务端回执
  useEffect(() => {
    const handler = (msg: MsgBase) => {
      if (msg.proto !== "MsgBugReport") return;
      const m = msg as MsgBugReport;
      if (m.result) setSubmit({ kind: "ok", path: m.path });
      else setSubmit({ kind: "fail", error: m.error });
    };
    eventBus.on("message", handler);
    return () => eventBus.off("message", handler);
  }, []);

  function handleSubmit() {
    const desc = description.trim();
    if (!desc || submit.kind === "sending") return;

    const s = useGameStore.getState();
    const clientInfo = JSON.stringify({
      meta: {
        ts: new Date().toISOString(),
        url: typeof window !== "undefined" ? window.location.href : "",
        userAgent: typeof navigator !== "undefined" ? navigator.userAgent : "",
        mode: s.mode,
        phase: s.phase,
        turnCount: s.turnCount,
        currentTurn: s.currentTurn,
        myName: s.myName,
        opponentName: s.opponentName,
      },
      gameStore: s, // 函数会被 JSON.stringify 自动忽略，仅保留数据字段
    });

    setSubmit({ kind: "sending" });
    const sent = NetManager.send({ proto: "MsgBugReport", description: desc, clientInfo } as MsgBugReport);
    if (!sent) setSubmit({ kind: "fail", error: "未连接服务器" });
  }

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50 p-4"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          onClick={() => setOpen(false)}
        >
          <motion.div
            className="w-full max-w-md rounded-lg border border-rose-400/40 bg-slate-950/95 p-5 shadow-2xl shadow-black/60"
            initial={{ scale: 0.92, y: 16 }}
            animate={{ scale: 1, y: 0 }}
            exit={{ scale: 0.92, y: 16 }}
            onClick={(e) => e.stopPropagation()}
          >
            <div className="mb-3 flex items-center justify-between">
              <h2 className="text-sm font-black text-rose-300">反馈 Bug（F2）</h2>
              <button
                onClick={() => setOpen(false)}
                className="rounded px-2 py-0.5 text-xs text-slate-400 transition-colors hover:text-white"
              >
                关闭
              </button>
            </div>

            <label className="block text-xs font-bold text-slate-300">问题描述</label>
            <textarea
              ref={textRef}
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={5}
              placeholder="描述触发 bug 的操作、现象、期望结果……提交时会自动附带当前对局全量信息。"
              className="mt-1.5 w-full resize-none rounded border border-slate-600 bg-slate-900 px-2.5 py-2 text-sm text-white placeholder:text-slate-500 focus:border-rose-400 focus:outline-none"
            />

            <div className="mt-3 flex items-center justify-between gap-3">
              <div className="min-w-0 flex-1 text-[11px]">
                {submit.kind === "ok" && (
                  <span className="text-emerald-400">
                    已提交并保存{submit.path ? `：${submit.path}` : ""}
                  </span>
                )}
                {submit.kind === "fail" && (
                  <span className="text-rose-400">提交失败{submit.error ? `：${submit.error}` : ""}</span>
                )}
                {submit.kind === "sending" && <span className="text-slate-400">提交中……</span>}
              </div>
              <button
                onClick={handleSubmit}
                disabled={!description.trim() || submit.kind === "sending"}
                className="shrink-0 rounded bg-rose-500 px-4 py-1.5 text-sm font-bold text-white transition-colors hover:bg-rose-400 disabled:cursor-not-allowed disabled:opacity-50"
              >
                提交反馈
              </button>
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
