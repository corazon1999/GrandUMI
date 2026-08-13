"use client";

/**
 * FeedbackOverlay — 大厅与游戏内共用的反馈窗口
 *
 * 按 F 弹出/关闭。大厅只附带基础环境信息，游戏内额外附带
 * gameStore 镜像，发送给服务端落盘。
 */

import { useEffect, useRef, useState } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { NetManager } from "@/net/NetManager";
import { eventBus } from "@/net/eventBus";
import { useGameStore } from "@/store/gameStore";
import { useNetStore } from "@/store/netStore";
import type { FeedbackCategory, MsgBase, MsgBugReport } from "@/types/net";

interface Props {
  context: "lobby" | "game";
  /** 外部入口每次递增该值即可打开反馈窗口；F 键仍可独立切换。 */
  openRequest?: number;
}

type SubmitState =
  | { kind: "idle" }
  | { kind: "sending" }
  | { kind: "ok" }
  | { kind: "fail"; error?: string };

const CATEGORY_CONFIG: Record<
  FeedbackCategory,
  { tab: string; label: string; placeholder: string }
> = {
  bug: {
    tab: "提交 Bug",
    label: "问题描述",
    placeholder: "描述触发 Bug 的操作、实际现象和期望结果……提交时会自动附带当前对局信息。",
  },
  suggestion: {
    tab: "优化建议",
    label: "建议内容",
    placeholder: "描述你希望优化的功能、操作体验或界面效果……",
  },
};

export default function FeedbackOverlay({ context, openRequest }: Props) {
  const [open, setOpen] = useState(false);
  const [category, setCategory] = useState<FeedbackCategory>("bug");
  const [drafts, setDrafts] = useState<Record<FeedbackCategory, string>>({
    bug: "",
    suggestion: "",
  });
  const [submit, setSubmit] = useState<SubmitState>({ kind: "idle" });
  const textRef = useRef<HTMLTextAreaElement>(null);
  const pendingCategoryRef = useRef<FeedbackCategory | null>(null);
  const lastOpenRequestRef = useRef(openRequest);
  const description = drafts[category];
  const config = CATEGORY_CONFIG[category];
  const title = context === "lobby" ? "问题反馈" : "游戏反馈（F）";
  const placeholder =
    category === "bug" && context === "lobby"
      ? "描述大厅中触发 Bug 的操作、实际现象和期望结果……提交时会自动附带当前页面信息。"
      : config.placeholder;

  // F 切换显隐；在输入区域打字时不抢占按键。
  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") {
        setOpen(false);
        return;
      }

      const target = e.target as HTMLElement | null;
      const isEditing =
        target?.tagName === "INPUT" ||
        target?.tagName === "TEXTAREA" ||
        target?.isContentEditable;
      if (
        e.code !== "KeyF" ||
        e.repeat ||
        e.ctrlKey ||
        e.altKey ||
        e.metaKey ||
        isEditing
      ) {
        return;
      }

      e.preventDefault();
      setOpen((visible) => !visible);
    }

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  // 左侧栏等外部入口仅负责请求打开；首次挂载不自动弹窗。
  useEffect(() => {
    if (openRequest === undefined || openRequest === lastOpenRequestRef.current) return;
    lastOpenRequestRef.current = openRequest;
    setOpen(true);
  }, [openRequest]);

  // 打开时聚焦、重置提交状态。
  useEffect(() => {
    if (!open) return;

    setSubmit((current) => (current.kind === "sending" ? current : { kind: "idle" }));
    const timer = setTimeout(() => textRef.current?.focus(), 50);
    return () => clearTimeout(timer);
  }, [open]);

  // 订阅服务端回执。
  useEffect(() => {
    const handler = (msg: MsgBase) => {
      if (msg.proto !== "MsgBugReport") return;

      const response = msg as MsgBugReport;
      if (response.result) {
        const submittedCategory = pendingCategoryRef.current;
        if (submittedCategory) {
          setDrafts((current) => ({ ...current, [submittedCategory]: "" }));
        }
        setSubmit({ kind: "ok" });
      } else {
        setSubmit({ kind: "fail", error: response.error });
      }
      pendingCategoryRef.current = null;
    };

    eventBus.on("message", handler);
    return () => eventBus.off("message", handler);
  }, []);

  function handleSubmit() {
    const trimmedDescription = description.trim();
    if (!trimmedDescription || submit.kind === "sending") return;

    const netState = useNetStore.getState();
    const gameState = context === "game" ? useGameStore.getState() : null;
    const clientInfo = JSON.stringify({
      meta: {
        ts: new Date().toISOString(),
        url: typeof window !== "undefined" ? window.location.href : "",
        userAgent: typeof navigator !== "undefined" ? navigator.userAgent : "",
        context,
        account: netState.account,
        playerName: netState.playerName,
        connectionState: netState.connState,
        networkDiagnostics: NetManager.getDiagnostics(),
        ...(gameState
          ? {
              mode: gameState.mode,
              phase: gameState.phase,
              turnCount: gameState.turnCount,
              currentTurn: gameState.currentTurn,
              myName: gameState.myName,
              opponentName: gameState.opponentName,
            }
          : {}),
      },
      // 大厅不附带上一局可能残留的对局数据。
      ...(gameState ? { gameStore: gameState } : {}),
    });

    setSubmit({ kind: "sending" });
    pendingCategoryRef.current = category;
    const sent = NetManager.send({
      proto: "MsgBugReport",
      category,
      description: trimmedDescription,
      clientInfo,
    } as MsgBugReport);

    if (!sent) {
      pendingCategoryRef.current = null;
      setSubmit({ kind: "fail", error: "未连接服务器" });
    }
  }

  function selectCategory(nextCategory: FeedbackCategory) {
    if (submit.kind === "sending") return;
    setCategory(nextCategory);
    setSubmit({ kind: "idle" });
    requestAnimationFrame(() => textRef.current?.focus());
  }

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          className="fixed inset-0 z-[60] flex items-center justify-center overflow-y-auto bg-black/50 px-[calc(1rem+var(--layout-safe-left,env(safe-area-inset-left)))] py-[calc(1rem+var(--layout-safe-top,env(safe-area-inset-top)))] [padding-bottom:calc(1rem+var(--layout-safe-bottom,env(safe-area-inset-bottom)))] [padding-right:calc(1rem+var(--layout-safe-right,env(safe-area-inset-right)))]"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          onClick={() => setOpen(false)}
        >
          <motion.div
            role="dialog"
            aria-modal="true"
            aria-label={title}
            className={`max-h-full w-full max-w-md overflow-y-auto rounded-lg border bg-slate-950/95 p-4 shadow-2xl shadow-black/60 sm:p-5 ${
              category === "bug" ? "border-rose-400/40" : "border-sky-400/40"
            }`}
            initial={{ scale: 0.92, y: 16 }}
            animate={{ scale: 1, y: 0 }}
            exit={{ scale: 0.92, y: 16 }}
            onClick={(event) => event.stopPropagation()}
          >
            <div className="mb-3 flex items-center justify-between">
              <h2 className="text-sm font-black text-white">{title}</h2>
              <button
                type="button"
                onClick={() => setOpen(false)}
                aria-label="关闭弹窗"
                className="flex min-h-11 min-w-11 items-center justify-center rounded px-2 text-xs text-slate-400 transition-colors hover:bg-slate-800 hover:text-white"
              >
                关闭
              </button>
            </div>

            <div className="mb-4 grid grid-cols-2 rounded-md bg-slate-900 p-1" role="tablist">
              {(Object.keys(CATEGORY_CONFIG) as FeedbackCategory[]).map((item) => {
                const selected = item === category;
                return (
                  <button
                    key={item}
                    type="button"
                    role="tab"
                    aria-selected={selected}
                    disabled={submit.kind === "sending"}
                    onClick={() => selectCategory(item)}
                    className={`min-h-11 rounded px-3 py-1.5 text-xs font-bold transition-colors disabled:cursor-wait ${
                      selected
                        ? item === "bug"
                          ? "bg-rose-500 text-white"
                          : "bg-sky-500 text-white"
                        : "text-slate-400 hover:text-white"
                    }`}
                  >
                    {CATEGORY_CONFIG[item].tab}
                  </button>
                );
              })}
            </div>

            <label className="block text-xs font-bold text-slate-300">{config.label}</label>
            <textarea
              ref={textRef}
              value={description}
              onChange={(event) =>
                setDrafts((current) => ({ ...current, [category]: event.target.value }))
              }
              maxLength={4000}
              rows={5}
              placeholder={placeholder}
              className={`mt-1.5 w-full resize-none rounded border border-slate-600 bg-slate-900 px-2.5 py-2 text-sm text-white placeholder:text-slate-500 focus:outline-none ${
                category === "bug" ? "focus:border-rose-400" : "focus:border-sky-400"
              }`}
            />

            <div className="mt-1 text-right text-[10px] text-slate-500">
              {description.length}/4000
            </div>

            <div className="mt-2 flex items-center justify-between gap-3">
              <div className="min-w-0 flex-1 text-[11px]">
                {submit.kind === "ok" && (
                  <span className="text-emerald-400">已提交并保存，感谢你的反馈</span>
                )}
                {submit.kind === "fail" && (
                  <span className="text-rose-400">
                    提交失败{submit.error ? `：${submit.error}` : ""}
                  </span>
                )}
                {submit.kind === "sending" && <span className="text-slate-400">提交中……</span>}
              </div>
              <button
                type="button"
                onClick={handleSubmit}
                disabled={!description.trim() || submit.kind === "sending"}
                className={`min-h-11 shrink-0 rounded px-4 py-1.5 text-sm font-bold text-white transition-colors disabled:cursor-not-allowed disabled:opacity-50 ${
                  category === "bug"
                    ? "bg-rose-500 hover:bg-rose-400"
                    : "bg-sky-500 hover:bg-sky-400"
                }`}
              >
                发送
              </button>
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
