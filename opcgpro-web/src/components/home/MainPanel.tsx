"use client";

import { useState, useRef, useEffect, type PointerEvent as ReactPointerEvent } from "react";
import NextImage from "next/image";
import LobbyPanel from "./LobbyPanel";
import DeckChoosePanel from "./DeckChoosePanel";
import PlayerListPanel from "./PlayerListPanel";
import InviteNotifyOverlay from "./InviteNotifyOverlay";
import FriendlyRoomPanel from "./FriendlyRoomPanel";
import HistoryPanel from "./HistoryPanel";
import CardCatalogPanel from "./CardCatalogPanel";
import NetStatePanel from "@/components/ui/NetStatePanel";
import { useNetStore } from "@/store/netStore";
import { getAllCachedCards, loadCardSet } from "@/data/CardLoader";
import { loadAllDecks } from "@/data/DeckMapper";
import { DEFAULT_SEARCH_SETS } from "@/data/cardSets";
import type { CardData } from "@/types/card";
import Modal from "@/components/ui/Modal";
import { thumbSrc } from "@/lib/sprite";
import { useVirtualList } from "@/hooks/useVirtualList";

type View = "lobby" | "deck" | "catalog" | "history";

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
      <p className="text-gray-500 text-[10px] mb-2">所有领航卡</p>
      {loading ? (
        <p className="text-gray-600 text-xs text-center py-8">加载中...</p>
      ) : (
        <AvatarGrid leaders={leaders} current={current} onSelect={onSelect} />
      )}
    </Modal>
  );
}

// 头像网格：虚拟滚动 + 缩略图 + 懒加载，避免一次性请求全部领航卡原图拖垮服务器。
// 单独组件，仅在 Modal 打开时挂载，确保 useVirtualList 正确绑定到滚动容器。
const AVATAR_ITEM = 56; // 单元 56px（w-14/h-14）
const AVATAR_GAP = 8;   // gap-2

function AvatarGrid({
  leaders,
  current,
  onSelect,
}: {
  leaders: CardData[];
  current: string;
  onSelect: (card: CardData) => void;
}) {
  const { containerRef, totalHeight, visibleItems } = useVirtualList({
    itemCount: leaders.length,
    itemHeight: AVATAR_ITEM,
    rowWidth: AVATAR_ITEM,
    columns: 5,
    gap: AVATAR_GAP,
  });

  return (
    <div ref={containerRef} className="h-80 overflow-y-auto">
      <div style={{ position: "relative", height: totalHeight }}>
        {visibleItems.map(({ index, row, col }) => {
          const card = leaders[index];
          if (!card) return null;
          const sprite = card.sprites?.length
            ? card.sprites[card.sprites.length - 1]
            : card.sprite ?? "";
          const isCurrent = sprite === current;
          return (
            <button
              key={card.number}
              onClick={() => onSelect(card)}
              style={{
                position: "absolute",
                top: row * (AVATAR_ITEM + AVATAR_GAP),
                left: col * (AVATAR_ITEM + AVATAR_GAP),
                width: AVATAR_ITEM,
                height: AVATAR_ITEM,
              }}
              className={`overflow-hidden rounded-full border-2 transition-all ${
                isCurrent
                  ? "border-orange-500 ring-2 ring-orange-500/40"
                  : "border-gray-700 hover:border-gray-400"
              }`}
              title={card.name}
            >
              {/* 用缩略图（约原图 15%）+ 原生懒加载；缩略图缺失则回退原图 */}
              <img
                src={thumbSrc(sprite)}
                alt={card.name}
                loading="lazy"
                decoding="async"
                draggable={false}
                className="h-full w-full object-cover object-top"
                style={{ transform: "scale(1.1)" }}
                onError={(e) => {
                  const img = e.currentTarget;
                  if (img.src.includes("-thumb/") && sprite) img.src = sprite;
                }}
              />
            </button>
          );
        })}
      </div>
    </div>
  );
}

// ── 在线人数徽标（可拖动，避免与聊天发送键重叠；位置持久化到 localStorage）────────────

const ONLINE_BADGE_POS_KEY = "grandumi_online_badge_pos";

function OnlineCountBadge({ onClick }: { onClick: () => void }) {
  const onlineCount = useNetStore((s) => s.onlineCount);
  const [pos, setPos] = useState<{ x: number; y: number } | null>(null);
  const btnRef = useRef<HTMLButtonElement>(null);
  const dragRef = useRef<{ startX: number; startY: number; baseX: number; baseY: number; moved: boolean } | null>(null);
  const lastPosRef = useRef<{ x: number; y: number } | null>(null);

  // 恢复上次拖到的位置
  useEffect(() => {
    try {
      const saved = localStorage.getItem(ONLINE_BADGE_POS_KEY);
      if (saved) setPos(JSON.parse(saved));
    } catch {}
  }, []);

  const onPointerDown = (e: ReactPointerEvent<HTMLButtonElement>) => {
    const rect = btnRef.current?.getBoundingClientRect();
    if (!rect) return;
    dragRef.current = { startX: e.clientX, startY: e.clientY, baseX: rect.left, baseY: rect.top, moved: false };
    btnRef.current?.setPointerCapture(e.pointerId);
  };

  const onPointerMove = (e: ReactPointerEvent<HTMLButtonElement>) => {
    const d = dragRef.current;
    if (!d) return;
    const dx = e.clientX - d.startX;
    const dy = e.clientY - d.startY;
    // 小于阈值视为点击抖动，不进入拖动
    if (!d.moved && Math.abs(dx) < 4 && Math.abs(dy) < 4) return;
    d.moved = true;
    const bw = btnRef.current?.offsetWidth ?? 0;
    const bh = btnRef.current?.offsetHeight ?? 0;
    const x = Math.min(Math.max(0, d.baseX + dx), window.innerWidth - bw);
    const y = Math.min(Math.max(0, d.baseY + dy), window.innerHeight - bh);
    const np = { x, y };
    lastPosRef.current = np;
    setPos(np);
  };

  const onPointerUp = (e: ReactPointerEvent<HTMLButtonElement>) => {
    const d = dragRef.current;
    dragRef.current = null;
    try { btnRef.current?.releasePointerCapture(e.pointerId); } catch {}
    if (!d) return;
    if (!d.moved) {
      onClick(); // 未拖动 → 视为点击，打开在线玩家列表
    } else if (lastPosRef.current) {
      try { localStorage.setItem(ONLINE_BADGE_POS_KEY, JSON.stringify(lastPosRef.current)); } catch {}
    }
  };

  return (
    <button
      ref={btnRef}
      type="button"
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerUp}
      title="查看在线玩家（可拖动）"
      style={pos ? { position: "fixed", left: pos.x, top: pos.y } : undefined}
      className={`${pos ? "" : "absolute bottom-3 right-4"} z-30 flex items-center gap-1.5 bg-gray-900/80 border border-gray-800 hover:border-green-600 rounded-full px-3 py-1 text-xs backdrop-blur-sm transition-colors cursor-grab active:cursor-grabbing touch-none select-none`}
    >
      <span className="w-2 h-2 rounded-full bg-green-400" />
      <span className="text-gray-300">
        在线 <span className="text-green-400 font-bold">{onlineCount}</span>
      </span>
    </button>
  );
}

const SELECTED_DECK_KEY = "grandumi_selected_deck";

export default function MainPanel() {
  const [view, setView] = useState<View>("lobby");
  const [showPlayerList, setShowPlayerList] = useState(false);
  const setGlobalDeck = useNetStore((s) => s.setSelectedDeck);
  const friendlyRoom = useNetStore((s) => s.friendlyRoom);

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
      leaderSprite: saved.leaderSprite,
      cards: [saved.leader, ...saved.cards].join("\n"),
    });
  }, []);

  // 进入友谊战房间后，大厅整体切换为房间界面
  if (friendlyRoom) {
    return (
      <>
        <FriendlyRoomPanel />
        <InviteNotifyOverlay />
      </>
    );
  }

  return (
    <div
      className="flex h-screen bg-gray-950"
      style={{
        paddingTop: "env(safe-area-inset-top)",
        paddingLeft: "env(safe-area-inset-left)",
        paddingRight: "env(safe-area-inset-right)",
        paddingBottom: "env(safe-area-inset-bottom)",
      }}
    >
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
        <button
          onClick={() => setView("catalog")}
          className={`w-10 h-10 rounded-xl text-xs font-bold transition-colors ${view === "catalog" ? "bg-orange-500 text-white" : "text-gray-400 hover:text-white hover:bg-gray-800"}`}
          title="卡牌图鉴"
        >
          图
        </button>
        <button
          onClick={() => setView("history")}
          className={`w-10 h-10 rounded-xl text-xs font-bold transition-colors ${view === "history" ? "bg-orange-500 text-white" : "text-gray-400 hover:text-white hover:bg-gray-800"}`}
          title="对局记录"
        >
          战
        </button>
        <div className="mt-auto">
          <NetStatePanel />
        </div>
      </nav>

      {/* 主内容区 */}
      <main className="relative flex-1 overflow-hidden">
        <OnlineCountBadge onClick={() => setShowPlayerList(true)} />
        {view === "lobby" && <LobbyPanel onGoToDeck={() => setView("deck")} />}
        {view === "deck" && <DeckChoosePanel onDeckSelected={() => setView("lobby")} />}
        {view === "catalog" && <CardCatalogPanel />}
        {view === "history" && <HistoryPanel />}
      </main>

      <PlayerListPanel open={showPlayerList} onClose={() => setShowPlayerList(false)} />
      <InviteNotifyOverlay />
    </div>
  );
}
