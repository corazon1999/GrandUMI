"use client";

import { useEffect, useState } from "react";
import {
  loadAllDecks,
  deleteDeck,
  getSpriteMap,
  getSelectedDeckName,
  setSelectedDeckName,
  subscribeDecksUpdated,
  type SavedDeck,
} from "@/data/DeckMapper";
import { useNetStore } from "@/store/netStore";
import { HomeRequest } from "@/net/HomeProtocol";
import Link from "next/link";
import Modal from "@/components/ui/Modal";
import { advanceImageFallback, CARD_BACK_SRC, thumbSrc } from "@/lib/sprite";

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
    const refresh = () => setDecks(loadAllDecks());
    refresh();
    const saved = getSelectedDeckName();
    if (saved) {
      setSelected(saved);
      syncToGlobal(saved);
    }
    return subscribeDecksUpdated(refresh);
  }, []);

  const handleSelect = (name: string) => {
    setSelected(name);
    setSelectedDeckName(name);
    syncToGlobal(name);
    HomeRequest.selectDeck(name);
    // 选择卡组后自动返回大厅
    onDeckSelected();
  };

  const handleDeleteConfirm = () => {
    if (!deleteTarget) return;
    deleteDeck(deleteTarget);
    HomeRequest.deleteDeck(deleteTarget);
    if (selected === deleteTarget) {
      setSelected("");
      setSelectedDeckName(null);
      setGlobalDeck(null);
    }
    setDecks(loadAllDecks());
    setDeleteTarget(null);
  };

  const deckEntries = Object.entries(decks);

  return (
    <div className="flex h-full flex-col gap-3 p-3 @[640px]:p-4">
      <div className="flex flex-col gap-3 @[640px]:flex-row @[640px]:items-center @[640px]:justify-between">
        <h2 className="text-white font-bold text-lg">我的卡组</h2>
        <div className="grid grid-cols-2 gap-2 @[640px]:flex @[640px]:items-center">
          <Link
            href="/deck-editor?new=1"
            className="flex min-h-11 items-center justify-center rounded-lg bg-emerald-600 px-4 text-sm font-bold text-white transition-colors hover:bg-emerald-500"
          >
            + 新建卡组
          </Link>
          <Link
            href="/deck-editor"
            className="flex min-h-11 items-center justify-center rounded-lg bg-orange-500 px-4 text-sm font-bold text-white transition-colors hover:bg-orange-400"
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
                className={`flex items-center gap-2 rounded-xl border-2 px-2 py-2 transition-all @[640px]:gap-3 @[640px]:px-4 @[640px]:py-3 ${
                  isSelected
                    ? "bg-orange-500/10 border-orange-500 shadow-lg shadow-orange-500/20"
                    : "bg-gray-800 border-gray-700 hover:border-gray-500"
                }`}
              >
                <button
                  type="button"
                  onClick={() => handleSelect(name)}
                  className="flex min-h-14 min-w-0 flex-1 items-center gap-3 rounded-lg text-left focus-visible:outline-2 focus-visible:outline-orange-400"
                >
                  {/* eslint-disable-next-line @next/next/no-img-element */}
                  <img
                    src={thumbSrc(deck.leaderSprite || CARD_BACK_SRC)}
                    alt={deck.leaderName}
                    className="h-14 w-10 shrink-0 rounded border border-gray-600 object-cover"
                    onError={(e) => advanceImageFallback(e.currentTarget, [deck.leaderSprite])}
                  />

                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-medium text-white">{name}</p>
                    <p className="truncate text-xs text-gray-400">{deck.leaderName}</p>
                    <div className="mt-1 flex gap-2 text-xs">
                      <span className="text-yellow-500">角 {deck.charCount}</span>
                      <span className="text-green-500">事 {deck.eventCount}</span>
                      <span className="text-purple-500">场 {deck.stageCount}</span>
                    </div>
                  </div>
                </button>

                <div className="flex flex-col items-center gap-1 shrink-0">
                  {isSelected && (
                    <span className="text-xs font-bold text-orange-400">
                      已选择
                    </span>
                  )}
                  <button
                    type="button"
                    onClick={(e) => {
                      e.stopPropagation();
                      setDeleteTarget(name);
                    }}
                    aria-label={`删除卡组 ${name}`}
                    className="min-h-11 rounded-lg px-3 text-sm text-gray-500 transition-colors hover:bg-gray-700 hover:text-red-400"
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
      <Modal
        open={Boolean(deleteTarget)}
        onClose={() => setDeleteTarget(null)}
        title="确认删除卡组？"
        mobileSheet
        maxWidthClass="max-w-sm"
      >
        <p className="truncate text-sm text-gray-300">「{deleteTarget}」</p>
        <p className="mt-2 text-sm text-gray-500">删除后无法恢复。</p>
        <div className="mt-5 grid grid-cols-2 gap-3">
          <button type="button" onClick={() => setDeleteTarget(null)} className="min-h-11 rounded-xl bg-gray-800 text-sm text-gray-300 hover:bg-gray-700">取消</button>
          <button type="button" onClick={handleDeleteConfirm} className="min-h-11 rounded-xl bg-red-600 text-sm font-bold text-white hover:bg-red-500">确认删除</button>
        </div>
      </Modal>
    </div>
  );
}
