"use client";

import { useState, useEffect, useRef, useCallback } from "react";
import { useRouter } from "next/navigation";
import { AnimatePresence } from "framer-motion";
import { useDeckStore, type DeckEntry } from "@/store/deckStore";
import { saveDeck, loadAllDecks, loadDeck, deleteDeck, type SavedDeck } from "@/data/DeckMapper";
import type { CardData } from "@/types/card";
import { toDisplayColor, primaryDisplayColor, COLOR_STYLES } from "@/lib/colorMap";
import CardHoverPreview, { type HoverInfo } from "./CardHoverPreview";

const HOVER_DELAY = 180;

type SaveState = "idle" | "saved" | "error";

export default function DeckInfoPanel() {
  const router = useRouter();
  const { leader, entries, totalCards, isValid, removeCard, clearDeck, setLeader, notice, clearNotice } =
    useDeckStore();
  const [deckName, setDeckName]     = useState("我的卡组");
  const [saveState, setSaveState]   = useState<SaveState>("idle");
  const [showLoad, setShowLoad]     = useState(false);
  const [savedDecks, setSavedDecks] = useState<Record<string, SavedDeck>>({});
  const [deleteTarget, setDeleteTarget] = useState<string | null>(null);
  const noticeTimer                 = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [hover, setHover]           = useState<HoverInfo | null>(null);
  const hoverTimer                  = useRef<ReturnType<typeof setTimeout> | null>(null);
  const total = totalCards();

  // info notice（自动移除卡牌提示）3秒后清除
  useEffect(() => {
    if (!notice || notice.type !== "info") return;
    if (noticeTimer.current) clearTimeout(noticeTimer.current);
    noticeTimer.current = setTimeout(() => clearNotice(), 3000);
    return () => { if (noticeTimer.current) clearTimeout(noticeTimer.current); };
  }, [notice, clearNotice]);

  // 加载已有卡组列表
  useEffect(() => {
    setSavedDecks(loadAllDecks());
  }, []);

  // 卡组条目变化时，若悬停的卡牌已不在卡组中则清除预览
  useEffect(() => {
    if (!hover) return;
    if (!entries.some((e) => e.card.number === hover.card.number)) {
      if (hoverTimer.current) clearTimeout(hoverTimer.current);
      setHover(null);
    }
  }, [entries, hover]);

  const handleSave = () => {
    if (!isValid()) return;
    try {
      const cards = entries.flatMap((e) => Array(e.count).fill(e.card) as CardData[]);
      saveDeck(deckName, leader!, cards);
      setSavedDecks(loadAllDecks());
      setSaveState("saved");
      setTimeout(() => setSaveState("idle"), 2000);
    } catch {
      setSaveState("error");
      setTimeout(() => setSaveState("idle"), 2000);
    }
  };

  const handleLoad = (name: string) => {
    const result = loadDeck(name);
    if (!result) return;
    clearDeck();
    setLeader(result.leader);
    // 把卡组里的卡片逐张加入 store
    const { addCard } = useDeckStore.getState();
    result.cards.forEach((c) => addCard(c));
    setDeckName(name);
    setShowLoad(false);
  };

  const handleDelete = (name: string) => {
    deleteDeck(name);
    setSavedDecks(loadAllDecks());
  };

  const handleMouseEnter = useCallback((card: CardData, rect: DOMRect, currentSprite: string) => {
    if (hoverTimer.current) clearTimeout(hoverTimer.current);
    hoverTimer.current = setTimeout(() => setHover({ card, rect, currentSprite }), HOVER_DELAY);
  }, []);

  const handleMouseLeave = useCallback(() => {
    if (hoverTimer.current) clearTimeout(hoverTimer.current);
    setHover(null);
  }, []);

  const remaining = 40 - total;

  return (
    <div className="flex flex-col h-full">
      {/* 标题栏 */}
      <div className="flex items-center justify-between px-3 py-2 border-b border-gray-800 shrink-0">
        <div className="flex items-center gap-2">
          <button
            onClick={() => router.push("/home")}
            className="text-gray-400 hover:text-white text-xs px-2 py-1 rounded hover:bg-gray-800 transition-colors"
            title="返回大厅"
          >
            ← 返回
          </button>
          <span className="text-white font-bold text-sm">卡组</span>
        </div>
        <div className="flex items-center gap-1">
          <button
            onClick={() => setShowLoad(!showLoad)}
            className="text-gray-400 hover:text-white text-xs px-2 py-1 rounded hover:bg-gray-800 transition-colors"
          >
            读取
          </button>
          <button
            onClick={clearDeck}
            className="text-gray-400 hover:text-red-400 text-xs px-2 py-1 rounded hover:bg-gray-800 transition-colors"
          >
            清空
          </button>
        </div>
      </div>

      {/* 读取卡组面板 */}
      {showLoad && (
        <div className="border-b border-gray-800 bg-gray-900 max-h-64 overflow-y-auto shrink-0">
          {Object.keys(savedDecks).length === 0 ? (
            <p className="text-gray-600 text-xs text-center py-4">暂无保存的卡组</p>
          ) : (
            Object.entries(savedDecks).map(([name, deck]) => (
              <div
                key={name}
                className="flex items-center gap-2 px-3 py-2 hover:bg-gray-800 border-b border-gray-800/50"
              >
                {/* 领航头像 */}
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img
                  src={deck.leaderSprite || "/sprites/CardBack.png"}
                  alt={deck.leaderName}
                  className="w-8 h-11 object-cover rounded border border-gray-700 shrink-0"
                  onError={(e) => { (e.target as HTMLImageElement).src = "/sprites/CardBack.png"; }}
                />
                <div className="flex-1 min-w-0">
                  <p className="text-white text-xs font-medium truncate">{name}</p>
                  <p className="text-gray-500 text-[10px] truncate">{deck.leaderName}</p>
                  <div className="flex gap-1.5 mt-0.5">
                    <span className="text-yellow-500 text-[9px]">角{deck.charCount}</span>
                    <span className="text-green-500 text-[9px]">事{deck.eventCount}</span>
                    <span className="text-purple-500 text-[9px]">场{deck.stageCount}</span>
                  </div>
                </div>
                <div className="flex flex-col gap-1 shrink-0">
                  <button
                    onClick={() => handleLoad(name)}
                    className="text-blue-400 hover:text-blue-300 text-[10px] px-1.5 py-0.5 rounded hover:bg-gray-700 transition-colors"
                  >
                    载入
                  </button>
                  <button
                    onClick={() => setDeleteTarget(name)}
                    className="text-red-500 hover:text-red-400 text-[10px] px-1.5 py-0.5 rounded hover:bg-gray-700 transition-colors"
                  >
                    删除
                  </button>
                </div>
              </div>
            ))
          )}
        </div>
      )}

      {/* 删除确认弹窗 */}
      {deleteTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60">
          <div className="bg-gray-900 border border-gray-700 rounded-xl p-5 w-72 shadow-2xl">
            <p className="text-white text-sm text-center mb-1">确认删除卡组？</p>
            <p className="text-gray-400 text-xs text-center mb-4 truncate">「{deleteTarget}」</p>
            <p className="text-gray-600 text-[10px] text-center mb-4">此操作不可撤销</p>
            <div className="flex gap-2">
              <button
                onClick={() => setDeleteTarget(null)}
                className="flex-1 py-2 rounded-lg bg-gray-800 text-gray-300 text-xs hover:bg-gray-700 transition-colors"
              >
                取消
              </button>
              <button
                onClick={() => { handleDelete(deleteTarget); setDeleteTarget(null); }}
                className="flex-1 py-2 rounded-lg bg-red-600 text-white text-xs font-bold hover:bg-red-500 transition-colors"
              >
                确认删除
              </button>
            </div>
          </div>
        </div>
      )}

      <div className="flex-1 overflow-y-auto flex flex-col min-h-0">
        {/* 卡组名称 */}
        <div className="px-3 pt-3 pb-2">
          <input
            className="w-full bg-gray-800 text-white text-sm rounded-lg px-3 py-2 outline-none border border-gray-700 focus:border-orange-500 transition-colors"
            value={deckName}
            onChange={(e) => setDeckName(e.target.value)}
            placeholder="卡组名称"
          />
        </div>

        {/* 领航卡 */}
        <div className="px-3 pb-3">
          <div className="flex items-center gap-2">
            <span className="text-gray-500 text-xs shrink-0">领航</span>
            {leader ? (
              <div className="flex items-center gap-2 flex-1 min-w-0">
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img
                  src={leader.sprite ?? "/sprites/CardBack.png"}
                  alt={leader.name}
                  className="w-10 h-14 object-cover rounded border border-gray-700"
                  onError={(e) => { (e.target as HTMLImageElement).src = "/sprites/CardBack.png"; }}
                />
                <div className="min-w-0">
                  <p className="text-white text-xs font-medium truncate">{leader.name}</p>
                  <p className={`text-[10px] font-bold ${COLOR_STYLES[primaryDisplayColor(leader.color)]?.text ?? "text-gray-400"}`}>
                    {toDisplayColor(leader.color)} · {leader.number}
                  </p>
                </div>
                <button
                  onClick={() => setLeader(null)}
                  className="text-gray-600 hover:text-red-400 text-xs shrink-0 ml-auto"
                >
                  ✕
                </button>
              </div>
            ) : (
              <div className="flex-1 h-14 rounded-lg border border-dashed border-gray-700 flex items-center justify-center">
                <p className="text-gray-600 text-xs">← 切换领航卡模式选择</p>
              </div>
            )}
          </div>
        </div>

        {/* 自动移除提示 */}
        {notice?.type === "info" && (
          <div className="mx-3 mb-2 px-2 py-1.5 rounded-lg bg-blue-900/60 border border-blue-800">
            <p className="text-blue-300 text-[10px] text-center">{notice.message}</p>
          </div>
        )}

        {/* 费用曲线 */}
        <div className="px-3 pb-2">
          <CostCurve entries={entries} />
        </div>

        {/* 张数统计 */}
        <div className="px-3 pb-2 flex items-center gap-2">
          <div className={`h-1.5 flex-1 rounded-full overflow-hidden bg-gray-800`}>
            <div
              className={`h-full rounded-full transition-all ${
                total === 40 ? "bg-green-500" : total > 40 ? "bg-red-500" : "bg-orange-500"
              }`}
              style={{ width: `${Math.min(100, (total / 40) * 100)}%` }}
            />
          </div>
          <span className={`text-xs font-bold shrink-0 ${
            total === 40 ? "text-green-400" : total > 40 ? "text-red-400" : "text-gray-400"
          }`}>
            {total}/40
          </span>
          {remaining > 0 && (
            <span className="text-gray-600 text-[10px] shrink-0">还差{remaining}张</span>
          )}
        </div>

        {/* 卡牌列表 */}
        <div className="flex-1 overflow-y-auto px-3 pb-2 flex flex-col gap-0.5 min-h-0">
          {entries.length === 0 ? (
            <p className="text-gray-700 text-xs text-center py-6">
              从搜索结果点击卡牌添加
            </p>
          ) : (
            entries.map((e) => (
              <DeckEntryRow
                key={e.card.number}
                entry={e}
                onRemove={removeCard}
                onMouseEnter={handleMouseEnter}
                onMouseLeave={handleMouseLeave}
              />
            ))
          )}
        </div>
      </div>

      {/* 悬停大图预览 */}
      <AnimatePresence>
        {hover && <CardHoverPreview info={hover} />}
      </AnimatePresence>

      {/* 保存按钮 */}
      <div className="px-3 py-3 border-t border-gray-800 shrink-0">
        <button
          onClick={handleSave}
          disabled={!isValid()}
          className={`w-full py-2.5 rounded-xl font-bold text-sm transition-all ${
            saveState === "saved"
              ? "bg-green-600 text-white"
              : saveState === "error"
                ? "bg-red-600 text-white"
                : isValid()
                  ? "bg-orange-500 hover:bg-orange-400 text-white"
                  : "bg-gray-800 text-gray-600 cursor-not-allowed"
          }`}
        >
          {saveState === "saved"
            ? "✓ 已保存"
            : saveState === "error"
              ? "保存失败"
              : isValid()
                ? "保存卡组"
                : `还需 ${!leader ? "选择领航卡" : `${remaining}张卡`}`}
        </button>
      </div>
    </div>
  );
}

// ── 费用曲线（炉石风格柱状图） ────────────────────────────────────────────────

const BAR_COLORS = [
  "bg-gradient-to-t from-emerald-600 to-emerald-400",   // 0
  "bg-gradient-to-t from-emerald-600 to-emerald-400",   // 1
  "bg-gradient-to-t from-teal-600 to-teal-400",          // 2
  "bg-gradient-to-t from-cyan-600 to-cyan-400",          // 3
  "bg-gradient-to-t from-blue-600 to-blue-400",          // 4
  "bg-gradient-to-t from-indigo-600 to-indigo-400",      // 5
  "bg-gradient-to-t from-violet-600 to-violet-400",      // 6
  "bg-gradient-to-t from-orange-600 to-orange-400",      // 7
  "bg-gradient-to-t from-red-600 to-red-400",            // 8
  "bg-gradient-to-t from-red-700 to-red-500",            // 9
];

function barColor(cost: number): string {
  return BAR_COLORS[Math.min(cost, BAR_COLORS.length - 1)] ?? BAR_COLORS[BAR_COLORS.length - 1];
}

function CostCurve({ entries }: { entries: DeckEntry[] }) {
  const costMap: Record<number, number> = {};
  entries.forEach((e) => {
    const c = e.card.cost;
    costMap[c] = (costMap[c] ?? 0) + e.count;
  });

  const costs     = Object.keys(costMap).map(Number).sort((a, b) => a - b);
  const maxCost   = costs.length > 0 ? Math.max(10, costs[costs.length - 1]) : 10;
  const maxCount  = Math.max(1, ...Object.values(costMap));
  const allCosts  = Array.from({ length: maxCost + 1 }, (_, i) => i);

  // 无卡牌时占位
  if (costs.length === 0) {
    return (
      <div className="flex flex-col gap-1">
        <span className="text-gray-500 text-[10px]">费用曲线</span>
        <div className="flex items-center justify-center h-12 bg-gray-800/50 rounded-lg">
          <span className="text-gray-700 text-[10px]">暂无卡牌</span>
        </div>
      </div>
    );
  }

  const CHART_H = 64; // h-16 = 64px
  const LABEL_H = 16; // x轴标签高度
  const NUM_H   = 14; // 数量数字高度

  return (
    <div className="flex flex-col gap-2">
      <span className="text-gray-500 text-[10px]">费用曲线</span>
      <div className="flex gap-px h-16 bg-gray-800/30 rounded-lg px-1">
        {allCosts.map((i) => {
          const count   = costMap[i] ?? 0;
          const ratio   = maxCount > 0 ? count / maxCount : 0;
          const barMax  = CHART_H - LABEL_H - (count > 0 ? NUM_H : 0);
          const barH    = count > 0 ? Math.max(4, Math.round(ratio * barMax)) : 0;
          return (
            <div key={i} className="flex-1 flex flex-col items-center" style={{ paddingTop: CHART_H - LABEL_H - barH - (count > 0 ? NUM_H : 0) }}>
              {/* 数量 */}
              <span className={`text-[9px] font-bold leading-none mb-0.5 ${
                count > 0 ? "text-white/90" : "text-transparent"
              }`}>
                {count > 0 ? count : " "}
              </span>
              {/* 柱体 */}
              <div
                className={`w-full rounded-t transition-all ${
                  count > 0 ? barColor(i) : "bg-transparent"
                }`}
                style={{ height: barH, minHeight: count > 0 ? 4 : 0 }}
              />
              {/* X轴 */}
              <span className={`text-[9px] leading-none mt-0.5 ${
                count > 0 ? "text-gray-400" : "text-gray-700"
              }`}>
                {i}
              </span>
            </div>
          );
        })}
      </div>
    </div>
  );
}

// ── 卡组条目行 ────────────────────────────────────────────────────────────

function DeckEntryRow({
  entry,
  onRemove,
  onMouseEnter,
  onMouseLeave,
}: {
  entry: DeckEntry;
  onRemove: (number: string) => void;
  onMouseEnter: (card: CardData, rect: DOMRect, currentSprite: string) => void;
  onMouseLeave: () => void;
}) {
  const sprite      = entry.card.sprite ?? "/sprites/CardBack.png";
  const primary     = primaryDisplayColor(entry.card.color);
  const colorStyle  = COLOR_STYLES[primary];

  return (
    <div
      className="flex items-center gap-1.5 py-1.5 px-2 border-b border-gray-800/60 group relative rounded-md overflow-hidden cursor-default"
      style={{
        backgroundImage: `url(${sprite})`,
        backgroundSize: "cover",
        backgroundPosition: "center 30%",
      }}
      onMouseEnter={(e) => onMouseEnter(entry.card, e.currentTarget.getBoundingClientRect(), sprite)}
      onMouseLeave={onMouseLeave}
    >
      {/* 半透明遮罩保证文字可读 */}
      <div className="absolute inset-0 bg-gray-950/70 group-hover:bg-gray-950/55 transition-colors" />

      {/* 费用圆形底图 */}
      <div className={`w-4 h-4 rounded-full flex items-center justify-center shrink-0 relative z-10 ${colorStyle?.bg ?? "bg-gray-600"}`}>
        <span
          className="text-white text-[9px] font-bold leading-none"
          style={{ textShadow: "0 0 2px rgba(0,0,0,0.85), 0 0 1px rgba(0,0,0,1)" }}
        >
          {entry.card.cost}
        </span>
      </div>
      <span className="text-white text-[11px] truncate flex-1 min-w-0 relative z-10 font-medium drop-shadow-sm">
        {entry.card.name}
      </span>
      <span className="text-gray-300 text-[10px] shrink-0 relative z-10">×{entry.count}</span>
      <button
        onClick={() => onRemove(entry.card.number)}
        className="text-gray-300 hover:text-red-400 text-xs opacity-0 group-hover:opacity-100 transition-all shrink-0 w-4 relative z-10 font-bold"
      >
        −
      </button>
    </div>
  );
}
