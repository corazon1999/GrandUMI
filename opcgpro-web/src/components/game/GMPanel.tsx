"use client";

/**
 * GMPanel — GM 调试面板
 *
 * 按 T 切换显隐（焦点在输入框时不触发切换）。
 * 第一功能：输入卡牌编号（如 OP01-001）加一张到自己手牌。
 */

import { useEffect, useRef, useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { GameRequest } from "@/net/GameRequest";
import { useGameStore } from "@/store/gameStore";
import { useLayoutQuarterTurn } from "@/components/ui/ResponsiveScope";

interface OP17CoverageCardResult {
  number: string;
  name: string;
  color: string;
  passed: boolean;
  triggers: string[];
  message: string;
}

interface OP17CoverageReport {
  color: string;
  total: number;
  passed: number;
  failed: number;
  results: OP17CoverageCardResult[];
  error?: string;
}

export default function GMPanel() {
  const [open, setOpen] = useState(false);
  const [cardNumber, setCardNumber] = useState("");
  const [lifeNumber, setLifeNumber] = useState("");
  const [lifeTarget, setLifeTarget] = useState<"self" | "opponent">("self");
  const [donCount, setDonCount] = useState("9");
  const [summonNumber, setSummonNumber] = useState("");
  const [summonTarget, setSummonTarget] = useState<"self" | "opponent">("self");
  const [coverageRunning, setCoverageRunning] = useState(false);
  const [coverageReports, setCoverageReports] = useState<Record<string, OP17CoverageReport>>({});
  const inputRef = useRef<HTMLInputElement>(null);
  const summonInputRef = useRef<HTMLInputElement>(null);
  const lastAction = useGameStore((s) => s.lastAction);
  const lastActionPayload = useGameStore((s) => s.lastActionPayloadObj);
  const rotateQuarterTurn = useLayoutQuarterTurn();

  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.key !== "t" && e.key !== "T") return;
      // 焦点在输入框/文本域时不抢按键
      const el = e.target as HTMLElement | null;
      const tag = el?.tagName;
      if (tag === "INPUT" || tag === "TEXTAREA" || el?.isContentEditable) return;
      e.preventDefault();
      setOpen((v) => !v);
    }
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  // 打开时自动聚焦输入框
  useEffect(() => {
    if (open) inputRef.current?.focus();
  }, [open]);

  useEffect(() => {
    if (lastAction === "DebugOP17CoverageStarted") {
      setCoverageRunning(true);
      return;
    }
    if (lastAction !== "DebugOP17CoverageResult" || !lastActionPayload) return;

    const color = String(lastActionPayload.color ?? "未知");
    const rawResults = Array.isArray(lastActionPayload.results) ? lastActionPayload.results : [];
    const results: OP17CoverageCardResult[] = rawResults.map((raw) => {
      const item = raw as Record<string, unknown>;
      return {
        number: String(item.number ?? ""),
        name: String(item.name ?? ""),
        color: String(item.color ?? color),
        passed: item.passed === true,
        triggers: Array.isArray(item.triggers) ? item.triggers.map(String) : [],
        message: String(item.message ?? ""),
      };
    });
    const report: OP17CoverageReport = {
      color,
      total: Number(lastActionPayload.total ?? results.length),
      passed: Number(lastActionPayload.passed ?? results.filter((item) => item.passed).length),
      failed: Number(lastActionPayload.failed ?? results.filter((item) => !item.passed).length),
      results,
      error: typeof lastActionPayload.error === "string" ? lastActionPayload.error : undefined,
    };
    setCoverageReports((current) => ({ ...current, [color]: report }));
    setCoverageRunning(false);
  }, [lastAction, lastActionPayload]);

  function submit() {
    const num = cardNumber.trim().toUpperCase();
    if (!num) return;
    GameRequest.debugAddCard(num);
    inputRef.current?.focus();
  }

  function submitDon() {
    const n = parseInt(donCount, 10);
    GameRequest.debugAddDon(Number.isFinite(n) && n > 0 ? n : 1);
  }

  function submitLife() {
    const num = lifeNumber.trim().toUpperCase();
    if (!num) return;
    GameRequest.debugAddLife(num, lifeTarget);
  }

  function refreshDon() {
    GameRequest.debugRefreshDon();
  }

  function submitSummon() {
    const num = summonNumber.trim().toUpperCase();
    if (!num) return;
    GameRequest.debugSummon(num, summonTarget);
    // 不清空输入框，便于连续召唤同一张；保持焦点
    summonInputRef.current?.focus();
  }

  function koAll(target: "self" | "opponent") {
    GameRequest.debugKoAll(target);
  }

  function restAll(target: "self" | "opponent") {
    GameRequest.debugRestAll(target);
  }

  function leaderAttack() {
    GameRequest.debugLeaderAttack();
  }

  function runOP17Coverage() {
    setCoverageRunning(true);
    GameRequest.debugRunOP17Coverage();
  }

  return (
    <>
      {/* 单人测试专用：左上角浮动 GM 按钮（移动端无 T 键时唤出面板） */}
      {rotateQuarterTurn && !open && (
        <button
          type="button"
          onClick={() => setOpen(true)}
          style={{
            left: "calc(1rem + var(--layout-safe-left, env(safe-area-inset-left)))",
            top: "calc(1rem + var(--layout-safe-top, env(safe-area-inset-top)))",
          }}
          className="fixed z-50 flex min-h-12 min-w-12 items-center justify-center rounded-md border border-amber-400/50 bg-amber-500/90 px-3 py-2 text-xs font-black text-slate-950 shadow-lg transition-colors hover:bg-amber-400 focus-visible:outline-2 focus-visible:outline-amber-200"
          aria-label="打开 GM 调试面板"
        >
          GM
        </button>
      )}

    <AnimatePresence>
      {open && (
        <motion.div
          style={{
            right: "calc(1rem + var(--layout-safe-right, env(safe-area-inset-right)))",
            top: "calc(1rem + var(--layout-safe-top, env(safe-area-inset-top)))",
            width: "min(18rem, calc(100cqw - 2rem - var(--layout-safe-left, 0px) - var(--layout-safe-right, 0px)))",
            maxHeight: "calc(100cqh - 2rem - var(--layout-safe-top, 0px) - var(--layout-safe-bottom, 0px))",
          }}
          className="fixed z-50 flex flex-col rounded-lg border border-amber-400/40 bg-slate-950/95 shadow-2xl shadow-black/50"
          initial={{ opacity: 0, x: 24 }}
          animate={{ opacity: 1, x: 0 }}
          exit={{ opacity: 0, x: 24 }}
        >
          <div className="flex shrink-0 items-center justify-between border-b border-white/10 px-4 py-2.5">
            <h2 className="text-sm font-black text-amber-300">GM 调试面板</h2>
            <button
              type="button"
              onClick={() => setOpen(false)}
              className="flex min-h-12 min-w-12 items-center justify-center rounded px-2 py-1 text-xs text-slate-400 transition-colors hover:text-white focus-visible:outline-2 focus-visible:outline-amber-200"
            >
              关闭 (T)
            </button>
          </div>

          {/* 内容区：随 GM 指令增多可上下滚动，避免底部指令被裁掉无法点击 */}
          <div className="min-h-0 flex-1 overflow-y-auto px-4 py-3">
          <label className="block text-xs font-bold text-amber-300">OP17 当前颜色全卡巡检</label>
          <button
            onClick={runOP17Coverage}
            disabled={coverageRunning}
            className={`mt-1.5 min-h-12 w-full rounded px-3 py-2 text-sm font-black transition-colors ${
              coverageRunning
                ? "cursor-wait bg-slate-800 text-slate-500"
                : "bg-emerald-500 text-slate-950 hover:bg-emerald-400"
            }`}
          >
            {coverageRunning ? "巡检中…" : "巡检当前领航颜色"}
          </button>
          <p className="mt-1.5 text-[11px] text-slate-500">
            在独立宽松场景中依次运行该颜色每张 OP17 卡的适用触发，不改变当前对局场面。
          </p>
          {Object.values(coverageReports).map((report) => (
            <details key={report.color} className="mt-2 rounded border border-white/10 bg-slate-900/80 px-2 py-1.5">
              <summary className={`cursor-pointer text-xs font-bold ${report.failed === 0 ? "text-emerald-300" : "text-rose-300"}`}>
                {report.color}色：{report.passed}/{report.total} 通过
              </summary>
              {report.error && <p className="mt-1 text-[10px] text-rose-300">{report.error}</p>}
              <div className="mt-1.5 max-h-44 space-y-1 overflow-y-auto pr-1">
                {report.results.map((result) => (
                  <div key={result.number} className="rounded bg-black/20 px-1.5 py-1 text-[10px] leading-4">
                    <span className={result.passed ? "text-emerald-300" : "text-rose-300"}>
                      {result.passed ? "✓" : "✕"} {result.number}
                    </span>
                    <span className="ml-1 text-slate-300">{result.name}</span>
                    <p className="text-slate-500">{result.triggers.join(" / ") || result.message}</p>
                    {!result.passed && <p className="text-rose-300">{result.message}</p>}
                  </div>
                ))}
              </div>
            </details>
          ))}

          <div className="my-2 h-px bg-white/10" />

          <label className="block text-xs font-bold text-slate-300">加牌到手牌</label>
          <div className="mt-1.5 flex gap-2">
            <input
              ref={inputRef}
              value={cardNumber}
              onChange={(e) => setCardNumber(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") submit();
              }}
              placeholder="例：OP01-001"
              className="min-w-0 flex-1 rounded border border-slate-600 bg-slate-900 px-2 py-1.5 text-sm text-white placeholder:text-slate-500 focus:border-amber-400 focus:outline-none"
            />
            <button
              onClick={submit}
              className="min-h-12 shrink-0 rounded bg-amber-500 px-3 py-1.5 text-sm font-bold text-slate-950 transition-colors hover:bg-amber-400"
            >
              添加
            </button>
          </div>
          <p className="mt-1.5 text-[11px] text-slate-500">
            输入卡牌编号后回车或点"添加"，可连续加牌。
          </p>

          <div className="my-2 h-px bg-white/10" />

          <label className="block text-xs font-bold text-slate-300">置于生命区顶端</label>
          <div className="mt-1.5 flex gap-1 rounded border border-slate-600 bg-slate-900 p-0.5">
            <button
              onClick={() => setLifeTarget("self")}
              className={`min-h-12 flex-1 rounded px-2 py-1 text-xs font-bold transition-colors ${
                lifeTarget === "self" ? "bg-amber-500 text-slate-950" : "text-slate-400 hover:text-white"
              }`}
            >
              我方生命
            </button>
            <button
              onClick={() => setLifeTarget("opponent")}
              className={`min-h-12 flex-1 rounded px-2 py-1 text-xs font-bold transition-colors ${
                lifeTarget === "opponent" ? "bg-amber-500 text-slate-950" : "text-slate-400 hover:text-white"
              }`}
            >
              对方生命
            </button>
          </div>
          <div className="mt-1.5 flex gap-2">
            <input
              value={lifeNumber}
              onChange={(e) => setLifeNumber(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") submitLife();
              }}
              placeholder="例：OP17-101"
              className="min-w-0 flex-1 rounded border border-slate-600 bg-slate-900 px-2 py-1.5 text-sm text-white placeholder:text-slate-500 focus:border-amber-400 focus:outline-none"
            />
            <button
              onClick={submitLife}
              className="min-h-12 shrink-0 rounded bg-amber-500 px-3 py-1.5 text-sm font-bold text-slate-950 transition-colors hover:bg-amber-400"
            >
              放入
            </button>
          </div>
          <p className="mt-1.5 text-[11px] text-slate-500">
            下一次该方领袖受到伤害时，可确定性验证这张卡牌的生命【触发】。
          </p>

          <div className="my-2 h-px bg-white/10" />

          <label className="block text-xs font-bold text-slate-300">加咚（活跃）</label>
          <div className="mt-1.5 flex gap-2">
            <input
              type="number"
              min={1}
              value={donCount}
              onChange={(e) => setDonCount(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") submitDon();
              }}
              className="w-20 min-w-0 rounded border border-slate-600 bg-slate-900 px-2 py-1.5 text-sm text-white focus:border-amber-400 focus:outline-none"
            />
            <button
              onClick={submitDon}
              className="min-h-12 flex-1 rounded bg-amber-500 px-3 py-1.5 text-sm font-bold text-slate-950 transition-colors hover:bg-amber-400"
            >
              加咚
            </button>
          </div>
          <button
            onClick={refreshDon}
            className="mt-1.5 min-h-12 w-full rounded bg-sky-600 px-3 py-1.5 text-sm font-bold text-white transition-colors hover:bg-sky-500"
          >
            刷新咚（全部回费用区并竖直）
          </button>

          <div className="my-2 h-px bg-white/10" />

          <label className="block text-xs font-bold text-slate-300">打出到场上</label>
          <div className="mt-1.5 flex gap-1 rounded border border-slate-600 bg-slate-900 p-0.5">
            <button
              onClick={() => setSummonTarget("self")}
              className={`min-h-12 flex-1 rounded px-2 py-1 text-xs font-bold transition-colors ${
                summonTarget === "self"
                  ? "bg-amber-500 text-slate-950"
                  : "text-slate-400 hover:text-white"
              }`}
            >
              我方场上
            </button>
            <button
              onClick={() => setSummonTarget("opponent")}
              className={`min-h-12 flex-1 rounded px-2 py-1 text-xs font-bold transition-colors ${
                summonTarget === "opponent"
                  ? "bg-amber-500 text-slate-950"
                  : "text-slate-400 hover:text-white"
              }`}
            >
              对方场上
            </button>
          </div>
          <div className="mt-1.5 flex gap-2">
            <input
              ref={summonInputRef}
              value={summonNumber}
              onChange={(e) => setSummonNumber(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") submitSummon();
              }}
              placeholder="例：OP01-025"
              className="min-w-0 flex-1 rounded border border-slate-600 bg-slate-900 px-2 py-1.5 text-sm text-white placeholder:text-slate-500 focus:border-amber-400 focus:outline-none"
            />
            <button
              onClick={submitSummon}
              className="min-h-12 shrink-0 rounded bg-amber-500 px-3 py-1.5 text-sm font-bold text-slate-950 transition-colors hover:bg-amber-400"
            >
              打出
            </button>
          </div>
          <p className="mt-1.5 text-[11px] text-slate-500">
            角色/舞台打出到场上，不扣费；己方卡牌会执行【登场时】效果（摆到对方场仅布置、不触发）。
          </p>

          <div className="my-2 h-px bg-white/10" />

          <label className="block text-xs font-bold text-slate-300">KO 场上角色</label>
          <div className="mt-1.5 flex gap-2">
            <button
              onClick={() => koAll("self")}
              className="min-h-12 flex-1 rounded bg-rose-600 px-3 py-1.5 text-sm font-bold text-white transition-colors hover:bg-rose-500"
            >
              KO 我方全部
            </button>
            <button
              onClick={() => koAll("opponent")}
              className="min-h-12 flex-1 rounded bg-rose-600 px-3 py-1.5 text-sm font-bold text-white transition-colors hover:bg-rose-500"
            >
              KO 对方全部
            </button>
          </div>
          <p className="mt-1.5 text-[11px] text-slate-500">
            KO 该方场上全部角色（不含领航/舞台），会触发【K.O.时】等效果。
          </p>

          <div className="my-2 h-px bg-white/10" />

          <label className="block text-xs font-bold text-slate-300">横置场上角色</label>
          <div className="mt-1.5 flex gap-2">
            <button
              onClick={() => restAll("self")}
              className="min-h-12 flex-1 rounded bg-orange-600 px-3 py-1.5 text-sm font-bold text-white transition-colors hover:bg-orange-500"
            >
              横置我方全部
            </button>
            <button
              onClick={() => restAll("opponent")}
              className="min-h-12 flex-1 rounded bg-orange-600 px-3 py-1.5 text-sm font-bold text-white transition-colors hover:bg-orange-500"
            >
              横置对方全部
            </button>
          </div>
          <p className="mt-1.5 text-[11px] text-slate-500">
            将该方场上全部角色转为横置（休息）状态，不含领航/舞台，不触发横置相关效果。
          </p>

          <div className="my-2 h-px bg-white/10" />

          <label className="block text-xs font-bold text-slate-300">对手领袖攻击</label>
          <button
            onClick={leaderAttack}
            className="mt-1.5 min-h-12 w-full rounded bg-purple-600 px-3 py-1.5 text-sm font-bold text-white transition-colors hover:bg-purple-500"
          >
            对手领袖攻击我方领袖
          </button>
          <p className="mt-1.5 text-[11px] text-slate-500">
            由对手领袖向我方领袖发起一次完整攻击，可正常宣告【阻挡者】/【反击】并结算领袖伤害。对手领袖会横置。
          </p>
          </div>
        </motion.div>
      )}
    </AnimatePresence>
    </>
  );
}
