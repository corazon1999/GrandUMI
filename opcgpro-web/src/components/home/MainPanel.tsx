"use client";

import { useState, useRef, useEffect } from "react";
import NextImage from "next/image";
import LobbyPanel from "./LobbyPanel";
import DeckChoosePanel from "./DeckChoosePanel";
import NetStatePanel from "@/components/ui/NetStatePanel";
import { useNetStore } from "@/store/netStore";
import { getAllCachedCards, loadCardSet } from "@/data/CardLoader";
import { loadAllDecks } from "@/data/DeckMapper";
import { DEFAULT_SEARCH_SETS } from "@/data/cardSets";
import type { CardData } from "@/types/card";
import Modal from "@/components/ui/Modal";

type View = "lobby" | "deck";

const AVATAR_KEY = "grandumi_avatar";

// 从缓存中找一个名为"路飞"的领航卡作为默认头像
function getDefaultAvatar(): string {
  if (typeof window === "undefined") return "";
  const all = getAllCachedCards();
  const luffy = all.find(
    (c) => c.type === "Leader" && c.name.includes("路飞")
  );
  return luffy?.sprite ?? "";
}

function loadAvatar(): string {
  if (typeof window === "undefined") return "";
  return localStorage.getItem(AVATAR_KEY) || getDefaultAvatar();
}

function PlayerAvatar() {
  const playerName = useNetStore((s) => s.playerName);
  const account = useNetStore((s) => s.account);
  const setPlayerName = useNetStore((s) => s.setPlayerName);

  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);

  const [avatarSrc, setAvatarSrc] = useState("");
  const [showPicker, setShowPicker] = useState(false);

  useEffect(() => {
    setAvatarSrc(loadAvatar());
  }, []);

  const startEdit = () => {
    setDraft(playerName);
    setEditing(true);
  };

  useEffect(() => {
    if (editing) inputRef.current?.focus();
  }, [editing]);

  const confirm = () => {
    const name = draft.trim();
    if (!name) { setEditing(false); return; }
    setPlayerName(name);
    if (typeof window !== "undefined" && account) {
      localStorage.setItem(`grandumi_nick_${account}`, name);
    }
    setEditing(false);
  };

  const cancel = () => setEditing(false);

  const handleSelectAvatar = (card: CardData) => {
    const sprite = card.sprites?.length
      ? card.sprites[card.sprites.length - 1]
      : card.sprite ?? "";
    setAvatarSrc(sprite);
    localStorage.setItem(AVATAR_KEY, sprite);
    setShowPicker(false);
  };

  return (
    <>
      {editing ? (
        <div className="w-14 flex flex-col items-center gap-1 px-1">
          <input
            ref={inputRef}
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") confirm();
              if (e.key === "Escape") cancel();
            }}
            onBlur={confirm}
            maxLength={16}
            className="w-full bg-gray-800 text-white text-[10px] rounded px-1 py-0.5 outline-none border border-orange-500 text-center"
          />
          <span className="text-gray-600 text-[8px]">Enter确认</span>
        </div>
      ) : (
        <div className="flex flex-col items-center gap-0.5 mb-1">
          {/* 头像 */}
          <button
            onClick={() => setShowPicker(true)}
            className="relative w-10 h-10 rounded-full overflow-hidden border-2 border-gray-700 hover:border-orange-500 transition-colors bg-gray-800 shrink-0"
            title="点击更换头像"
          >
            {avatarSrc ? (
              <NextImage
                src={avatarSrc}
                alt="头像"
                fill
                sizes="40px"
                className="object-cover object-top rounded-full"
                style={{ transform: "scale(1.1)" }}
                draggable={false}
                onError={() => setAvatarSrc("")}
              />
            ) : (
              <span className="text-white text-xs font-bold">
                {playerName ? playerName[0].toUpperCase() : "?"}
              </span>
            )}
          </button>
          {/* 昵称 */}
          <button
            onClick={startEdit}
            title="点击修改昵称"
            className="text-gray-500 hover:text-gray-300 text-[9px] truncate w-14 text-center transition-colors"
          >
            {playerName || "未知"}
          </button>
        </div>
      )}

      {/* 头像选择弹窗 */}
      <AvatarPicker
        open={showPicker}
        onClose={() => setShowPicker(false)}
        onSelect={handleSelectAvatar}
        current={avatarSrc}
      />
    </>
  );
}

// ── 头像选择器 ──────────────────────────────────────────────────────────────

function AvatarPicker({
  open,
  onClose,
  onSelect,
  current,
}: {
  open: boolean;
  onClose: () => void;
  onSelect: (card: CardData) => void;
  current: string;
}) {
  const [leaders, setLeaders] = useState<CardData[]>([]);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!open) return;
    async function load() {
      setLoading(true);
      const allCached = getAllCachedCards();
      if (allCached.length === 0) {
        // 尚未加载过，先加载默认卡集
        for (const setName of DEFAULT_SEARCH_SETS) {
          await loadCardSet(setName).catch(() => {});
        }
      }
      const all = getAllCachedCards();
      setLeaders(all.filter((c) => c.type === "Leader"));
      setLoading(false);
    }
    load();
  }, [open]);

  return (
    <Modal open={open} onClose={onClose} title="选择头像">
      <div className="flex flex-col gap-2 max-h-80 overflow-y-auto">
        <p className="text-gray-500 text-[10px]">所有领航卡</p>
        {loading ? (
          <p className="text-gray-600 text-xs text-center py-8">加载中...</p>
        ) : (
        <div className="grid grid-cols-5 gap-2">
          {leaders.map((card) => {
            const sprite =
              card.sprites?.length
                ? card.sprites[card.sprites.length - 1]
                : card.sprite ?? "";
            const isCurrent = sprite === current;
            return (
              <button
                key={card.number}
                onClick={() => onSelect(card)}
                className={`relative w-14 h-14 rounded-full overflow-hidden border-2 transition-all ${
                  isCurrent
                    ? "border-orange-500 ring-2 ring-orange-500/40"
                    : "border-gray-700 hover:border-gray-400"
                }`}
                title={card.name}
              >
                <NextImage
                  src={sprite}
                  alt={card.name}
                  fill
                  sizes="56px"
                  className="object-cover object-top rounded-full"
                  style={{ transform: "scale(1.1)" }}
                  draggable={false}
                  onError={() => {}}
                />
              </button>
            );
          })}
        </div>
        )}
      </div>
    </Modal>
  );
}

const SELECTED_DECK_KEY = "grandumi_selected_deck";

export default function MainPanel() {
  const [view, setView] = useState<View>("lobby");
  const setGlobalDeck = useNetStore((s) => s.setSelectedDeck);

  // 启动时恢复已保存的卡组选择到全局 store
  // 直接从 SavedDeck 构建卡组字符串，不依赖卡牌缓存（缓存可能尚未加载）
  useEffect(() => {
    const name = localStorage.getItem(SELECTED_DECK_KEY);
    if (!name) return;
    const allDecks = loadAllDecks();
    const saved = allDecks[name];
    if (!saved) return;
    setGlobalDeck({
      name,
      leader: saved.leader,
      leaderName: saved.leaderName,
      cards: [saved.leader, ...saved.cards].join("\n"),
    });
  }, []);

  return (
    <div className="flex h-screen bg-gray-950">
      {/* 侧边导航 */}
      <nav className="w-16 bg-gray-900 border-r border-gray-800 flex flex-col items-center py-4 gap-3">
        <PlayerAvatar />
        <button
          onClick={() => setView("lobby")}
          className={`w-10 h-10 rounded-xl text-xs font-bold transition-colors ${view === "lobby" ? "bg-orange-500 text-white" : "text-gray-400 hover:text-white hover:bg-gray-800"}`}
          title="大厅"
        >
          厅
        </button>
        <button
          onClick={() => setView("deck")}
          className={`w-10 h-10 rounded-xl text-xs font-bold transition-colors ${view === "deck" ? "bg-orange-500 text-white" : "text-gray-400 hover:text-white hover:bg-gray-800"}`}
          title="卡组"
        >
          组
        </button>
        <div className="mt-auto">
          <NetStatePanel />
        </div>
      </nav>

      {/* 主内容区 */}
      <main className="flex-1 overflow-hidden">
        {view === "lobby" && <LobbyPanel onGoToDeck={() => setView("deck")} />}
        {view === "deck" && <DeckChoosePanel onDeckSelected={() => setView("lobby")} />}
      </main>
    </div>
  );
}
