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
type MobilePanel = "cards" | "deck";

const GRID_COLS_KEY = "deckEditor_gridCols";

export default function DeckEditorPage() {
  const [loadState, setLoadState] = useState<LoadState>("loading");
  const [loaded, setLoaded]       = useState(0);
  const [mobilePanel, setMobilePanel] = useState<MobilePanel>("cards");
  const [mobileFiltersOpen, setMobileFiltersOpen] = useState(false);
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
      <div
        className="flex h-screen flex-col items-center justify-center bg-gray-950 gap-4"
        style={{ height: "100dvh" }}
      >
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
      <div
        className="flex h-screen items-center justify-center bg-gray-950"
        style={{ height: "100dvh" }}
      >
        <p className="text-red-400">卡牌数据加载失败，请刷新页面重试</p>
      </div>
    );
  }

  return (
    <div
      data-deck-editor-page
      className="relative box-border flex h-screen flex-col overflow-hidden bg-gray-950 transition-[padding] duration-200 md:flex-row"
      style={{
        height: "100dvh",
        paddingTop: "max(var(--layout-safe-top, env(safe-area-inset-top)), var(--global-announcement-height, 0px))",
      }}
    >
      {/* 手机竖屏主导航：牌池与卡组各自占满可用宽度，避免三栏互相挤压。 */}
      <nav
        data-deck-mobile-nav
        className="grid h-12 shrink-0 grid-cols-[auto_auto_1fr_1fr] border-b border-gray-800 bg-gray-950 md:hidden"
        aria-label="卡组编辑视图"
      >
        <a
          href="/home"
          data-deck-mobile-back
          className="flex min-h-11 min-w-11 items-center justify-center border-r border-gray-800 px-2 text-sm text-gray-400 transition-colors hover:bg-gray-900 hover:text-white"
          title="返回大厅"
          aria-label="返回大厅"
        >
          ←
        </a>
        <button
          type="button"
          onClick={() => setMobileFiltersOpen(true)}
          className="px-4 text-xs font-bold text-orange-300 transition-colors hover:bg-gray-900"
          aria-expanded={mobileFiltersOpen}
          aria-controls="deck-mobile-filters"
        >
          ☰ 筛选
        </button>
        <button
          type="button"
          onClick={() => setMobilePanel("cards")}
          className={`border-l border-gray-800 text-sm font-bold transition-colors ${
            mobilePanel === "cards" ? "bg-gray-900 text-white" : "text-gray-500"
          }`}
          aria-pressed={mobilePanel === "cards"}
        >
          牌池
        </button>
        <button
          type="button"
          onClick={() => setMobilePanel("deck")}
          className={`border-l border-gray-800 text-sm font-bold transition-colors ${
            mobilePanel === "deck" ? "bg-gray-900 text-white" : "text-gray-500"
          }`}
          aria-pressed={mobilePanel === "deck"}
        >
          卡组
        </button>
      </nav>

      {/* 桌面保持左侧筛选栏；手机端按需覆盖展开。 */}
      <aside
        id="deck-mobile-filters"
        data-deck-search-panel
        className={`${mobileFiltersOpen ? "absolute inset-x-0 bottom-0 top-12 z-40 flex" : "hidden"} w-full shrink-0 border-r border-gray-800 bg-gray-950 md:static md:flex md:w-48`}
      >
        <div className="min-h-0 min-w-0 flex-1">
          <SearchPanel onClose={() => setMobileFiltersOpen(false)} />
        </div>
      </aside>

      <main
        data-deck-card-pool
        className={`${mobilePanel === "cards" ? "block" : "hidden"} min-h-0 min-w-0 flex-1 overflow-hidden pb-[env(safe-area-inset-bottom)] md:block md:pb-0`}
      >
        <SearchResultPanel />
      </main>
      <aside
        data-deck-editor-panel
        className={`${mobilePanel === "deck" ? "block" : "hidden"} min-h-0 min-w-0 flex-1 overflow-hidden border-gray-800 pb-[env(safe-area-inset-bottom)] md:block md:w-96 md:flex-none md:border-l`}
      >
        <DeckInfoPanel />
      </aside>
    </div>
  );
}
