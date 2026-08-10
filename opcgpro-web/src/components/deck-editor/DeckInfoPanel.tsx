"use client";

import { useState, useEffect, useRef, useCallback } from "react";
import { useRouter } from "next/navigation";
import { AnimatePresence } from "framer-motion";
import { useDeckStore, type DeckEntry } from "@/store/deckStore";
import { useNetStore } from "@/store/netStore";
import { saveDeck, loadAllDecks, loadDeck, deleteDeck, deckExists, nextDeckName, exportDeckString, importDeckString, getSelectedDeckName, subscribeDecksUpdated, type SavedDeck } from "@/data/DeckMapper";
import { HomeRequest } from "@/net/HomeProtocol";
import type { CardData } from "@/types/card";
import { toDisplayColor, primaryDisplayColor, COLOR_STYLES } from "@/lib/colorMap";
import { advanceImageFallback, CARD_BACK_SRC, thumbSrc } from "@/lib/sprite";
import { downloadGeneratedDeckImage, generateDeckImage } from "@/lib/deckImageExport";
import CardHoverPreview, { type HoverInfo } from "./CardHoverPreview";

const HOVER_DELAY = 180;

type SaveState = "idle" | "saved" | "error";
type ImageExportState = "idle" | "exporting" | "error";

interface DeckImagePreview {
  url: string;
  filename: string;
  width: number;
  height: number;
}

export default function DeckInfoPanel() {
  const router = useRouter();
  const { leader, entries, totalCards, isValid, removeCard, addCard, clearDeck, setLeader, getMainSize, notice, clearNotice } =
    useDeckStore();
  const [deckName, setDeckName]     = useState("我的卡组");
  const [saveState, setSaveState]   = useState<SaveState>("idle");
  const [showLoad, setShowLoad]     = useState(false);
  const [savedDecks, setSavedDecks] = useState<Record<string, SavedDeck>>({});
  const [deleteTarget, setDeleteTarget] = useState<string | null>(null);
  // 当前编辑内容的来源卡组名（载入过/已保存过的名字）；新建时为 null。
  // 用于判断「保存目标名」是否撞了别的已存在卡组，从而决定是否需要二次确认覆盖。
  const [loadedName, setLoadedName] = useState<string | null>(null);
  const [overwriteTarget, setOverwriteTarget] = useState<string | null>(null);
  const [showExport, setShowExport] = useState(false);
  const [showImport, setShowImport] = useState(false);
  const [exportText, setExportText] = useState("");
  const [importText, setImportText] = useState("");
  const [importMsg, setImportMsg]   = useState<string | null>(null);
  const [copied, setCopied]         = useState(false);
  const [imageExportState, setImageExportState] = useState<ImageExportState>("idle");
  const [imagePreview, setImagePreview] = useState<DeckImagePreview | null>(null);
  const imagePreviewUrl             = useRef<string | null>(null);
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
    const refresh = () => setSavedDecks(loadAllDecks());
    refresh();
    return subscribeDecksUpdated(refresh);
  }, []);

  // 初始化卡组名称：新建模式 → "新卡组"；否则带入当前已选卡组名
  useEffect(() => {
    const isNew = new URLSearchParams(window.location.search).get("new") === "1";
    if (isNew) {
      setDeckName(nextDeckName());
      setLoadedName(null); // 新建明确无来源卡组，防软导航残留旧 loadedName 导致保存时静默覆盖
    } else {
      const sel = getSelectedDeckName();
      if (sel) { setDeckName(sel); setLoadedName(sel); }
    }
  }, []);

  // 卡组条目变化时，若悬停的卡牌已不在卡组中则清除预览
  useEffect(() => {
    if (!hover) return;
    if (!entries.some((e) => e.card.number === hover.card.number)) {
      if (hoverTimer.current) clearTimeout(hoverTimer.current);
      setHover(null);
    }
  }, [entries, hover]);

  useEffect(() => () => {
    if (imagePreviewUrl.current) URL.revokeObjectURL(imagePreviewUrl.current);
  }, []);

  const handleNew = () => {
    clearDeck();
    clearNotice();
    setDeckName(nextDeckName());
    setLoadedName(null);
    setShowLoad(false);
  };

  const doSave = (name: string) => {
    try {
      const cards = entries.flatMap((e) => Array(e.count).fill(e.card) as CardData[]);
      const saved = saveDeck(name, leader!, cards);
      if (!useNetStore.getState().loggedIn || !HomeRequest.saveDeck(saved)) {
        throw new Error("未登录，云端同步请求发送失败");
      }
      setSavedDecks(loadAllDecks());
      setLoadedName(name);
      setSaveState("saved");
      setTimeout(() => setSaveState("idle"), 2000);
    } catch {
      setSaveState("error");
      setTimeout(() => setSaveState("idle"), 2000);
    }
  };

  const handleSave = () => {
    if (!isValid()) return;
    // 目标名已存在 → 仅当确实在编辑同名来源卡组(loadedName===deckName)时才静默覆盖自己；
    // 新建(loadedName===null)或改成别的已存在名都必须二次确认，杜绝「新建卡组误覆盖已有卡组」。
    if (deckExists(deckName) && (loadedName === null || deckName !== loadedName)) {
      setOverwriteTarget(deckName);
      return;
    }
    doSave(deckName);
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
    setLoadedName(name);
    setShowLoad(false);
  };

  const handleDelete = (name: string) => {
    deleteDeck(name);
    if (useNetStore.getState().loggedIn) HomeRequest.deleteDeck(name);
    setSavedDecks(loadAllDecks());
  };

  const handleExport = () => {
    if (!leader) {
      setExportText("⚠ 请先选择领航卡再导出");
    } else {
      const cards = entries.flatMap((e) => Array(e.count).fill(e.card) as CardData[]);
      setExportText(exportDeckString(leader, cards, deckName));
    }
    setShowExport(true);
    setShowImport(false);
    setShowLoad(false);
    setCopied(false);
  };

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(exportText);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      setCopied(false);
    }
  };

  const handleImageExport = async () => {
    if (!leader || entries.length === 0 || imageExportState === "exporting") return;
    setImageExportState("exporting");
    try {
      const generated = await generateDeckImage({ deckName, leader, entries });
      const url = URL.createObjectURL(generated.blob);
      if (imagePreviewUrl.current) URL.revokeObjectURL(imagePreviewUrl.current);
      imagePreviewUrl.current = url;
      setImagePreview({
        url,
        filename: generated.filename,
        width: generated.width,
        height: generated.height,
      });
      setImageExportState("idle");
    } catch (error) {
      console.error("导出卡组一图流失败", error);
      setImageExportState("error");
      window.setTimeout(() => setImageExportState("idle"), 3000);
    }
  };

  const closeImagePreview = () => {
    if (imagePreviewUrl.current) URL.revokeObjectURL(imagePreviewUrl.current);
    imagePreviewUrl.current = null;
    setImagePreview(null);
  };

  const handleImageDownload = () => {
    if (!imagePreview) return;
    downloadGeneratedDeckImage(imagePreview.url, imagePreview.filename);
  };

  const handleImportApply = () => {
    const { leader: lead, cards, skipped } = importDeckString(importText);
    if (!lead && cards.length === 0) {
      setImportMsg("没有识别到有效卡牌,请检查卡组码");
      return;
    }
    clearDeck();
    if (lead) setLeader(lead);
    const { addCard: add } = useDeckStore.getState();
    cards.forEach((c) => add(c));
    setImportMsg(`导入完成:${cards.length} 张${skipped > 0 ? `,跳过 ${skipped} 张无效卡号` : ""}${!lead ? "(未识别到领航)" : ""}`);
    setTimeout(() => { setShowImport(false); setImportText(""); setImportMsg(null); }, 1800);
  };

  const handleMouseEnter = useCallback((card: CardData, rect: DOMRect, currentSprite: string) => {
    if (hoverTimer.current) clearTimeout(hoverTimer.current);
    hoverTimer.current = setTimeout(() => setHover({ card, rect, currentSprite }), HOVER_DELAY);
  }, []);

  const handleMouseLeave = useCallback(() => {
    if (hoverTimer.current) clearTimeout(hoverTimer.current);
    setHover(null);
  }, []);

  const mainSize = getMainSize();
  const remaining = mainSize - total;

  return (
    <div className="flex flex-col h-full">
      {/* 标题栏 */}
      <div className="border-b border-gray-800 shrink-0">
        <div
          data-deck-toolbar-heading
          className="flex items-center px-3 pt-2 pr-16"
        >
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
        </div>
        <div
          data-deck-toolbar-actions
          className="grid grid-cols-5 gap-1 px-3 pb-2 pr-16"
        >
          <button
            onClick={handleNew}
            className="w-full text-emerald-400 hover:text-emerald-300 text-xs px-1 py-1 rounded hover:bg-gray-800 transition-colors"
          >
            新建
          </button>
          <button
            onClick={() => setShowLoad(!showLoad)}
            className="w-full text-gray-400 hover:text-white text-xs px-1 py-1 rounded hover:bg-gray-800 transition-colors"
          >
            读取
          </button>
          <button
            onClick={clearDeck}
            className="w-full text-gray-400 hover:text-red-400 text-xs px-1 py-1 rounded hover:bg-gray-800 transition-colors"
          >
            清空
          </button>
          <button
            onClick={handleExport}
            className="w-full text-sky-400 hover:text-sky-300 text-xs px-1 py-1 rounded hover:bg-gray-800 transition-colors"
          >
            导出
          </button>
          <button
            onClick={() => { setShowImport((v) => !v); setShowExport(false); setShowLoad(false); setImportMsg(null); }}
            className="w-full text-sky-400 hover:text-sky-300 text-xs px-1 py-1 rounded hover:bg-gray-800 transition-colors"
          >
            导入
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
                  src={thumbSrc(deck.leaderSprite || CARD_BACK_SRC)}
                  alt={deck.leaderName}
                  className="w-8 h-11 object-cover rounded border border-gray-700 shrink-0"
                  onError={(e) => advanceImageFallback(e.currentTarget, [deck.leaderSprite])}
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

      {/* 导出卡组面板 */}
      {showExport && (
        <div className="border-b border-gray-800 bg-gray-900 p-3 shrink-0">
          <textarea
            readOnly
            value={exportText}
            onClick={(e) => (e.target as HTMLTextAreaElement).select()}
            className="w-full h-32 bg-gray-800 text-gray-200 text-[11px] rounded-lg p-2 outline-none border border-gray-700 resize-none font-mono"
          />
          <div className="flex gap-2 mt-2">
            <button
              onClick={handleCopy}
              className="flex-1 py-1.5 rounded-lg bg-orange-500 hover:bg-orange-400 text-white text-xs font-bold transition-colors"
            >
              {copied ? "✓ 已复制" : "复制到剪贴板"}
            </button>
            <button
              onClick={() => setShowExport(false)}
              className="px-3 py-1.5 rounded-lg bg-gray-800 text-gray-400 text-xs hover:bg-gray-700 transition-colors"
            >
              关闭
            </button>
          </div>
        </div>
      )}

      {/* 导入卡组面板 */}
      {showImport && (
        <div className="border-b border-gray-800 bg-gray-900 p-3 shrink-0">
          <textarea
            value={importText}
            onChange={(e) => setImportText(e.target.value)}
            placeholder={"粘贴卡组码…\n支持两种格式:\n· 每行『数量 卡号』(本站导出格式)\n· 紧凑『数量x卡号』如 1xOP13-002 3xOP13-007 …(领航自动识别)"}
            className="w-full h-32 bg-gray-800 text-gray-200 text-[11px] rounded-lg p-2 outline-none border border-gray-700 focus:border-orange-500 resize-none font-mono"
          />
          {importMsg && <p className="text-emerald-400 text-[11px] mt-1.5">{importMsg}</p>}
          <div className="flex gap-2 mt-2">
            <button
              onClick={handleImportApply}
              disabled={!importText.trim()}
              className={`flex-1 py-1.5 rounded-lg text-xs font-bold transition-colors ${importText.trim() ? "bg-orange-500 hover:bg-orange-400 text-white" : "bg-gray-800 text-gray-600 cursor-not-allowed"}`}
            >
              导入
            </button>
            <button
              onClick={() => { setShowImport(false); setImportText(""); setImportMsg(null); }}
              className="px-3 py-1.5 rounded-lg bg-gray-800 text-gray-400 text-xs hover:bg-gray-700 transition-colors"
            >
              关闭
            </button>
          </div>
        </div>
      )}

      {/* 一图流预览弹窗 */}
      {imagePreview && (
        <div
          className="fixed inset-0 z-[10010] flex items-center justify-center bg-black/85 p-3 sm:p-6"
          onClick={closeImagePreview}
          data-testid="deck-image-preview-backdrop"
        >
          <div
            role="dialog"
            aria-modal="true"
            aria-labelledby="deck-image-preview-title"
            className="flex h-[94vh] w-full max-w-6xl flex-col overflow-hidden rounded-2xl border border-gray-700 bg-gray-950 shadow-2xl"
            onClick={(event) => event.stopPropagation()}
            data-testid="deck-image-preview"
          >
            <div className="flex shrink-0 items-center justify-between gap-3 border-b border-gray-800 px-4 py-3">
              <div className="min-w-0">
                <h2 id="deck-image-preview-title" className="truncate text-sm font-bold text-white">
                  一图流预览 · {deckName.trim() || "未命名卡组"}
                </h2>
                <p className="mt-0.5 text-[10px] text-gray-500">
                  {imagePreview.width} × {imagePreview.height} PNG
                </p>
              </div>
              <button
                onClick={closeImagePreview}
                aria-label="关闭一图流预览"
                className="mr-12 grid h-11 w-11 shrink-0 place-items-center rounded-lg bg-gray-900 text-gray-400 transition-colors hover:bg-gray-800 hover:text-white sm:mr-0"
              >
                ✕
              </button>
            </div>

            <div className="min-h-0 flex-1 overflow-hidden bg-black/40 p-3 sm:p-5">
              <div className="relative h-full w-full">
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img
                  src={imagePreview.url}
                  alt={`${deckName.trim() || "未命名卡组"} 一图流预览`}
                  className="absolute inset-0 m-auto block h-auto max-h-full w-auto max-w-full rounded-lg object-contain shadow-xl"
                />
              </div>
            </div>

            <div className="flex shrink-0 items-center justify-between gap-3 border-t border-gray-800 bg-gray-950 px-4 py-3">
              <p className="hidden text-[11px] text-gray-500 sm:block">预览不会自动下载，确认后可保存 PNG</p>
              <div className="ml-auto flex gap-2">
                <button
                  onClick={closeImagePreview}
                  className="min-h-11 rounded-lg bg-gray-800 px-4 py-2 text-xs text-gray-300 transition-colors hover:bg-gray-700"
                >
                  关闭
                </button>
                <button
                  onClick={handleImageDownload}
                  className="min-h-11 rounded-lg bg-orange-500 px-4 py-2 text-xs font-bold text-white transition-colors hover:bg-orange-400"
                  data-testid="deck-image-download"
                >
                  下载 PNG
                </button>
              </div>
            </div>
          </div>
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

      {/* 覆盖同名卡组确认弹窗（方案A：仅当目标名撞了别的已存在卡组时才弹，防新建/改名误覆盖） */}
      {overwriteTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60">
          <div className="bg-gray-900 border border-gray-700 rounded-xl p-5 w-72 shadow-2xl">
            <p className="text-white text-sm text-center mb-1">已存在同名卡组</p>
            <p className="text-gray-400 text-xs text-center mb-4 truncate">「{overwriteTarget}」</p>
            <p className="text-gray-600 text-[10px] text-center mb-4">继续保存将覆盖它，原内容不可恢复</p>
            <div className="flex gap-2">
              <button
                onClick={() => setOverwriteTarget(null)}
                className="flex-1 py-2 rounded-lg bg-gray-800 text-gray-300 text-xs hover:bg-gray-700 transition-colors"
              >
                取消
              </button>
              <button
                onClick={() => { doSave(overwriteTarget); setOverwriteTarget(null); }}
                className="flex-1 py-2 rounded-lg bg-orange-600 text-white text-xs font-bold hover:bg-orange-500 transition-colors"
              >
                覆盖保存
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
                  src={thumbSrc(leader.sprite ?? CARD_BACK_SRC)}
                  alt={leader.name}
                  className="w-10 h-14 object-cover rounded border border-gray-700"
                  onError={(e) => advanceImageFallback(e.currentTarget, [leader.sprite, leader.image])}
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
                total === mainSize ? "bg-green-500" : total > mainSize ? "bg-red-500" : "bg-orange-500"
              }`}
              style={{ width: `${Math.min(100, (total / mainSize) * 100)}%` }}
            />
          </div>
          <span className={`text-xs font-bold shrink-0 ${
            total === mainSize ? "text-green-400" : total > mainSize ? "text-red-400" : "text-gray-400"
          }`}>
            {total}/{mainSize}
          </span>
          {remaining > 0 && (
            <span className="text-gray-600 text-[10px] shrink-0">还差{remaining}张</span>
          )}
        </div>

        {/* 卡牌列表 */}
        <div className="px-3 pb-2 flex flex-col gap-0.5">
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
                onAdd={addCard}
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

      {/* 图片导出与保存 */}
      <div className="px-3 py-3 border-t border-gray-800 shrink-0 flex flex-col gap-2">
        <button
          onClick={handleImageExport}
          disabled={!leader || entries.length === 0 || imageExportState === "exporting"}
          title="把当前异画、卡牌数量和卡号导出为一张 PNG 图片"
          className={`w-full py-2 rounded-xl border text-xs font-bold transition-all ${
            leader && entries.length > 0 && imageExportState !== "exporting"
              ? imageExportState === "error"
                ? "border-red-500/60 bg-red-500/10 text-red-300"
                : "border-sky-500/40 bg-sky-500/10 text-sky-300 hover:border-sky-400 hover:bg-sky-500/20"
              : "border-gray-800 bg-gray-900 text-gray-600 cursor-not-allowed"
          }`}
        >
          {imageExportState === "exporting"
            ? "正在生成预览…"
            : imageExportState === "error"
              ? "生成失败，请重试"
              : "▦ 导出一图流"}
        </button>
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
                : `还需 ${!leader ? "选择领航卡" : remaining > 0 ? `${remaining}张卡` : `减少${-remaining}张卡`}`}
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
  onAdd,
  onMouseEnter,
  onMouseLeave,
}: {
  entry: DeckEntry;
  onRemove: (number: string) => void;
  onAdd: (card: CardData) => void;
  onMouseEnter: (card: CardData, rect: DOMRect, currentSprite: string) => void;
  onMouseLeave: () => void;
}) {
  const sprite      = entry.card.sprite ?? CARD_BACK_SRC;
  const primary     = primaryDisplayColor(entry.card.color);
  const colorStyle  = COLOR_STYLES[primary];

  return (
    <div
      className="flex items-center gap-1.5 py-1.5 px-2 border-b border-gray-800/60 group relative rounded-md overflow-hidden cursor-pointer"
      style={{
        backgroundImage: `url(${thumbSrc(sprite)})`,
        backgroundSize: "cover",
        backgroundPosition: "center 30%",
      }}
      onClick={() => onRemove(entry.card.number)}
      onMouseEnter={(e) => onMouseEnter(entry.card, e.currentTarget.getBoundingClientRect(), sprite)}
      onMouseLeave={onMouseLeave}
      title="点击减少一张"
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
        onClick={(e) => { e.stopPropagation(); onAdd(entry.card); }}
        className="text-gray-300 hover:text-green-400 text-sm opacity-0 group-hover:opacity-100 transition-all shrink-0 w-4 relative z-10 font-bold text-center leading-none"
        title="点击增加一张"
      >
        +
      </button>
    </div>
  );
}
