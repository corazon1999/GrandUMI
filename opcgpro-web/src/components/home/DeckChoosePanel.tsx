"use client";

import { useEffect, useState } from "react";
import { loadAllDecks, deleteDeck, type SavedDeck } from "@/data/DeckMapper";
import { getSpriteMap } from "@/data/DeckMapper";
import { useNetStore } from "@/store/netStore";
import Link from "next/link";

const SELECTED_KEY = "grandumi_selected_deck";

export default function DeckChoosePanel({ onDeckSelected }: { onDeckSelected: () => void }) {
  const [decks, setDecks] = useState<Record<string, SavedDeck>>({});
  const [selected, setSelected] = useState<string>("");
  const [deleteTarget, setDeleteTarget] = useState<string | null>(null);

  const setGlobalDeck = useNetStore((s) => s.setSelectedDeck);

  /**
   * 将选中的卡组同步到全局 store
   * 直接从 SavedDeck（localStorage）构建卡组字符串，不依赖卡牌缓存
   * 避免启动时卡牌数据尚未加载导致同步失败
   */
  const syncToGlobal = (name: string) => {
    const allDecks = loadAllDecks();
    const saved = allDecks[name];
    if (!saved) return;
    // 直接从编号构建卡组字符串，无需查询 cardCache
    const cardsStr = [saved.leader, ...saved.cards].join("\n");
    setGlobalDeck({
      name,
      leader: saved.leader,
      leaderName: saved.leaderName,
      leaderSprite: saved.leaderSprite,
      cards: cardsStr,
    });

    // 将异画映射写入 sessionStorage，供对局初始化时还原
    const spriteMap = getSpriteMap(name);
    if (typeof window !== "undefined") {
      sessionStorage.setItem("grandumi_spriteMap", JSON.stringify(spriteMap));
    }
  };

  useEffect(() => {
    setDecks(loadAllDecks());
    const saved = localStorage.getItem(SELECTED_KEY);
    if (saved) {
      setSelected(saved);
      syncToGlobal(saved);
    }
  }, []);

  const handleSelect = (name: string) => {
    setSelected(name);
    localStorage.setItem(SELECTED_KEY, name);
    syncToGlobal(name);
    // 选择卡组后自动返回大厅
    onDeckSelected();
  };

  const handleDeleteConfirm = () => {
    if (!deleteTarget) return;
    deleteDeck(deleteTarget);
    if (selected === deleteTarget) {
      setSelected("");
      localStorage.removeItem(SELECTED_KEY);
      setGlobalDeck(null);
    }
    setDecks(loadAllDecks());
    setDeleteTarget(null);
  };

  const deckEntries = Object.entries(decks);

  return (
    <div className="p-4 flex flex-col gap-3 h-full">
      <div className="flex items-center justify-between">
        <h2 className="text-white font-bold text-lg">我的卡组</h2>
        <div className="flex items-center gap-2">
          <Link
            href="/deck-editor?new=1"
            className="bg-emerald-600 hover:bg-emerald-500 text-white text-sm font-bold px-4 py-1.5 rounded-lg transition-colors"
          >
            + 新建卡组
          </Link>
          <Link
            href="/deck-editor"
            className="bg-orange-500 hover:bg-orange-400 text-white text-sm font-bold px-4 py-1.5 rounded-lg transition-colors"
          >
            编辑卡组
          </Link>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto flex flex-col gap-2">
        {deckEntries.length === 0 ? (
          <p className="text-gray-500 text-sm text-center mt-8">
            还没有卡组，去编辑器创建一副吧
          </p>
        ) : (
          deckEntries.map(([name, deck]) => {
            const isSelected = selected === name;
            return (
              <div
                key={name}
                className={`flex items-center gap-3 rounded-lg px-4 py-3 border-2 transition-all cursor-pointer ${
                  isSelected
                    ? "bg-orange-500/10 border-orange-500 shadow-lg shadow-orange-500/20"
                    : "bg-gray-800 border-gray-700 hover:border-gray-500"
                }`}
                onClick={() => handleSelect(name)}
              >
                {/* 领航头像 */}
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img
                  src={deck.leaderSprite || "/sprites/CardBack.png"}
                  alt={deck.leaderName}
                  className="w-10 h-14 object-cover rounded border border-gray-600 shrink-0"
                  onError={(e) => {
                    (e.target as HTMLImageElement).src = "/sprites/CardBack.png";
                  }}
                />

                <div className="flex-1 min-w-0">
                  <p className="text-white text-sm font-medium truncate">
                    {name}
                  </p>
                  <p className="text-gray-400 text-xs truncate">
                    {deck.leaderName}
                  </p>
                  <div className="flex gap-2 mt-1">
                    <span className="text-yellow-500 text-[10px]">
                      角 {deck.charCount}
                    </span>
                    <span className="text-green-500 text-[10px]">
                      事 {deck.eventCount}
                    </span>
                    <span className="text-purple-500 text-[10px]">
                      场 {deck.stageCount}
                    </span>
                  </div>
                </div>

                <div className="flex flex-col items-center gap-1 shrink-0">
                  {isSelected && (
                    <span className="text-orange-400 text-[10px] font-bold">
                      已选择
                    </span>
                  )}
                  <button
                    onClick={(e) => {
                      e.stopPropagation();
                      setDeleteTarget(name);
                    }}
                    className="text-gray-600 hover:text-red-400 text-[10px] px-1.5 py-0.5 rounded hover:bg-gray-700 transition-colors"
                  >
                    删除
                  </button>
                </div>
              </div>
            );
          })
        )}
      </div>

      {/* 删除确认弹窗 */}
      {deleteTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60">
          <div className="bg-gray-900 border border-gray-700 rounded-xl p-5 w-72 shadow-2xl">
            <p className="text-white text-sm text-center mb-1">确认删除卡组？</p>
            <p className="text-gray-400 text-xs text-center mb-4 truncate">
              「{deleteTarget}」
            </p>
            <p className="text-gray-600 text-[10px] text-center mb-4">
              此操作不可撤销
            </p>
            <div className="flex gap-2">
              <button
                onClick={() => setDeleteTarget(null)}
                className="flex-1 py-2 rounded-lg bg-gray-800 text-gray-300 text-xs hover:bg-gray-700 transition-colors"
              >
                取消
              </button>
              <button
                onClick={handleDeleteConfirm}
                className="flex-1 py-2 rounded-lg bg-red-600 text-white text-xs font-bold hover:bg-red-500 transition-colors"
              >
                确认删除
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
