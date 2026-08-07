"use client";

import { useEffect, useState } from "react";
import { loadAllCards, loadCardSet } from "@/data/CardLoader";
import { getSelectedDeckName, loadDeck } from "@/data/DeckMapper";
import { DEFAULT_SEARCH_SETS, ALL_SET_NAMES } from "@/data/cardSets";
import { useDeckStore } from "@/store/deckStore";
import SearchPanel from "@/components/deck-editor/SearchPanel";
import SearchResultPanel from "@/components/deck-editor/SearchResultPanel";
import DeckInfoPanel from "@/components/deck-editor/DeckInfoPanel";

type LoadState = "loading" | "done" | "error";

const GRID_COLS_KEY = "deckEditor_gridCols";

export default function DeckEditorPage() {
  const [loadState, setLoadState] = useState<LoadState>("loading");
  const [loaded, setLoaded]       = useState(0);
  const total = DEFAULT_SEARCH_SETS.length;

  useEffect(() => {
    // 从 localStorage 恢复列数设置
    const saved = parseInt(localStorage.getItem(GRID_COLS_KEY) ?? "", 10);
    if (!isNaN(saved)) useDeckStore.getState().setGridColumns(saved);

    async function load() {
      try {
        // 快速通道：一次请求合并单包（永久缓存，重复访问秒进）
        try {
          await loadAllCards();
          setLoaded(total);
        } catch {
          // 单包缺失/失败时回退：并行逐集加载，进度条随每个完成递增
          await Promise.all(
            DEFAULT_SEARCH_SETS.map((setName) =>
              loadCardSet(setName)
                .then(() => setLoaded((n) => n + 1))
                .catch(() => { setLoaded((n) => n + 1); }),
            ),
          );
          const remaining = ALL_SET_NAMES.filter((s) => !DEFAULT_SEARCH_SETS.includes(s));
          await Promise.all(remaining.map((s) => loadCardSet(s).catch(() => {})));
        }
        setLoadState("done");

        // 新建模式（?new=1）：开空白卡组，不自动载入已选卡组
        const isNew = new URLSearchParams(window.location.search).get("new") === "1";
        if (isNew) {
          useDeckStore.getState().clearDeck();
        } else {
          // 自动加载主页已选中的卡组
          const selectedDeck = getSelectedDeckName();
          if (selectedDeck) {
            const deck = loadDeck(selectedDeck);
            if (deck) {
              const store = useDeckStore.getState();
              store.clearDeck();
              store.setLeader(deck.leader);
              deck.cards.forEach((c) => store.addCard(c));
            }
          }
        }
      } catch {
        setLoadState("error");
      }
    }
    load();
  }, []);

  if (loadState === "loading") {
    const pct = Math.round((loaded / total) * 100);
    return (
      <div className="flex flex-col items-center justify-center h-screen bg-gray-950 gap-4">
        <p className="text-white font-bold text-lg">加载卡牌数据...</p>
        <div className="w-64 h-2 bg-gray-800 rounded-full overflow-hidden">
          <div className="h-full bg-orange-500 rounded-full transition-all duration-300"
               style={{ width: `${pct}%` }} />
        </div>
        <p className="text-gray-500 text-sm">{loaded} / {total} 个卡集</p>
      </div>
    );
  }

  if (loadState === "error") {
    return (
      <div className="flex items-center justify-center h-screen bg-gray-950">
        <p className="text-red-400">卡牌数据加载失败，请刷新页面重试</p>
      </div>
    );
  }

  return (
    <div className="flex h-screen bg-gray-950 overflow-hidden">
      {/* SearchPanel 宽度减少 1/4：原 w-64(256px) → w-48(192px) */}
      <div className="w-48 border-r border-gray-800 shrink-0">
        <SearchPanel />
      </div>
      <div className="flex-1 overflow-hidden">
        <SearchResultPanel />
      </div>
      <div className="w-80 border-l border-gray-800 shrink-0">
        <DeckInfoPanel />
      </div>
    </div>
  );
}
