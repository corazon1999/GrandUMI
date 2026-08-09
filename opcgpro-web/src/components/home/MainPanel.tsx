"use client";

import { useState, useRef, useEffect } from "react";
import NextImage from "next/image";
import LobbyPanel from "./LobbyPanel";
import DeckChoosePanel from "./DeckChoosePanel";
import PlayerListPanel from "./PlayerListPanel";
import FriendsPanel from "./FriendsPanel";
import InviteNotifyOverlay from "./InviteNotifyOverlay";
import FriendlyRoomPanel from "./FriendlyRoomPanel";
import LeaderLeaderboardPanel from "./LeaderLeaderboardPanel";
import HistoryPanel from "./HistoryPanel";
import CardCatalogPanel from "./CardCatalogPanel";
import ChangelogModal from "./ChangelogModal";
import ChatPanel from "./ChatPanel";
import NetStatePanel from "@/components/ui/NetStatePanel";
import { useNetStore } from "@/store/netStore";
import { HomeRequest } from "@/net/HomeProtocol";
import { LATEST_CHANGELOG } from "@/data/changelog";
import { getAllCachedCards, loadCardSet } from "@/data/CardLoader";
import { DEFAULT_SEARCH_SETS } from "@/data/cardSets";
import type { CardData } from "@/types/card";
import Modal from "@/components/ui/Modal";
import { advanceImageFallback, thumbSrc } from "@/lib/sprite";
import { useVirtualList } from "@/hooks/useVirtualList";
import LayoutPreviewFrame from "./LayoutPreviewFrame";
import { useLayoutSettings } from "./LayoutSettingsProvider";
import ProfilePanel from "./ProfilePanel";

type View = "lobby" | "deck" | "catalog" | "leaderboard" | "history" | "profile";
type AvatarVariant = "sidebar" | "header" | "profile";

// 从缓存中找一个名为"路飞"的领航卡作为默认头像
function getDefaultAvatar(): string {
  if (typeof window === "undefined") return "";
  const all = getAllCachedCards();
  const luffy = all.find(
    (c) => c.type === "Leader" && c.name.includes("路飞")
  );
  return luffy?.sprite ?? "";
}

function PlayerAvatar({ variant = "sidebar" }: { variant?: AvatarVariant }) {
  const playerName = useNetStore((s) => s.playerName);
  const cloudAvatar = useNetStore((s) => s.avatar);
  const setPlayerName = useNetStore((s) => s.setPlayerName);

  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);

  const [avatarSrc, setAvatarSrc] = useState("");
  const [showPicker, setShowPicker] = useState(false);

  useEffect(() => {
    setAvatarSrc(cloudAvatar || getDefaultAvatar());
  }, [cloudAvatar]);

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
    if (!HomeRequest.updateProfile(name, avatarSrc)) {
      useNetStore.getState().setError("网络未连接，昵称仅在本次页面生效");
    }
    setEditing(false);
  };

  const cancel = () => setEditing(false);

  const handleSelectAvatar = (card: CardData) => {
    const sprite = card.sprites?.length
      ? card.sprites[card.sprites.length - 1]
      : card.sprite ?? "";
    setAvatarSrc(sprite);
    if (!HomeRequest.updateProfile(playerName, sprite)) {
      useNetStore.getState().setError("网络未连接，头像仅在本次页面生效");
    }
    setShowPicker(false);
  };

  const avatarSize = variant === "profile" ? "h-16 w-16" : variant === "header" ? "h-11 w-11" : "h-10 w-10";
  const imageSize = variant === "profile" ? "64px" : variant === "header" ? "44px" : "40px";

  return (
    <>
      {editing ? (
        <div className={variant === "profile" ? "flex w-full items-center gap-2" : "flex w-20 flex-col items-center gap-1 px-1"}>
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
            aria-label="玩家昵称"
            className={`${variant === "profile" ? "h-11 flex-1 px-3 text-base" : "w-full px-1 py-1 text-xs"} rounded-lg border border-orange-500 bg-gray-800 text-center text-white outline-none`}
          />
          {variant !== "profile" && <span className="text-[10px] text-gray-600">Enter确认</span>}
          {variant === "profile" && (
            <button type="button" onClick={confirm} className="h-11 rounded-lg bg-orange-500 px-4 text-sm font-bold text-white">
              保存
            </button>
          )}
        </div>
      ) : (
        <div className={variant === "profile" ? "flex items-center gap-4" : variant === "header" ? "flex items-center" : "mb-1 flex flex-col items-center gap-1"}>
          <button
            type="button"
            onClick={() => setShowPicker(true)}
            aria-label="更换头像"
            className={`relative ${avatarSize} shrink-0 overflow-hidden rounded-full border-2 border-gray-700 bg-gray-800 transition-colors hover:border-orange-500 focus-visible:outline-2 focus-visible:outline-orange-400`}
          >
            {avatarSrc ? (
              <NextImage
                src={thumbSrc(avatarSrc)}
                alt="头像"
                fill
                sizes={imageSize}
                className="object-cover object-top rounded-full"
                style={{ transform: "scale(1.1)" }}
                draggable={false}
                onError={(event) => advanceImageFallback(event.currentTarget, [avatarSrc])}
              />
            ) : (
              <span className="text-white text-xs font-bold">
                {playerName ? playerName[0].toUpperCase() : "?"}
              </span>
            )}
          </button>
          {variant !== "header" && (
            <div className={variant === "profile" ? "min-w-0 flex-1" : "w-20"}>
              {variant === "profile" && <p className="mb-1 text-sm text-gray-500">当前玩家</p>}
              <button
                type="button"
                onClick={startEdit}
                className={`${variant === "profile" ? "min-h-11 w-full text-left text-lg font-bold text-white" : "min-h-8 w-full text-center text-[11px] text-gray-500"} truncate rounded-lg transition-colors hover:text-orange-300 focus-visible:outline-2 focus-visible:outline-orange-400`}
              >
                {playerName || "未知"}
              </button>
            </div>
          )}
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
    <Modal open={open} onClose={onClose} title="选择头像" mobileSheet maxWidthClass="max-w-md">
      <p className="mb-3 text-sm text-gray-500">从领航卡中选择头像</p>
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
    columns: 4,
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
                onError={(event) => advanceImageFallback(event.currentTarget, [sprite, card.image])}
              />
            </button>
          );
        })}
      </div>
    </div>
  );
}

// ── 左侧常驻在线人数入口 ──────────────────────────────────────────────────

function OnlineCountBadge({ onClick }: { onClick: () => void }) {
  const onlineCount = useNetStore((s) => s.onlineCount);

  return (
    <button
      type="button"
      onClick={onClick}
      title="查看在线玩家"
      className="flex min-h-11 w-full items-center justify-center gap-1.5 rounded-lg border border-gray-800 bg-gray-950/60 px-2 py-2 text-xs transition-colors hover:border-green-600 hover:bg-gray-800"
    >
      <span className="w-2 h-2 rounded-full bg-green-400" />
      <span className="text-gray-300">
        在线 <span className="text-green-400 font-bold">{onlineCount}</span>
      </span>
    </button>
  );
}

function MobileNavIcon({ view }: { view: Exclude<View, "history"> }) {
  if (view === "lobby") {
    return <path d="M4 11.5 12 4l8 7.5M6.5 10v9h11v-9M10 19v-5h4v5" />;
  }
  if (view === "deck") {
    return <><rect x="6" y="4" width="12" height="16" rx="2" /><path d="m9 8 3-2 3 2-1 4h-4L9 8Z" /></>;
  }
  if (view === "catalog") {
    return <><circle cx="10.5" cy="10.5" r="5.5" /><path d="m15 15 5 5" /></>;
  }
  if (view === "leaderboard") {
    return <><path d="M5 20v-6h4v6M10 20V8h4v12M15 20V4h4v16" /><path d="M3 20h18" /></>;
  }
  return <><circle cx="12" cy="8" r="4" /><path d="M4.5 20c.8-4.2 3.3-6 7.5-6s6.7 1.8 7.5 6" /></>;
}

function changelogSeenKey(account: string): string {
  return `grandumi_changelog_seen_${encodeURIComponent(account)}`;
}

export default function MainPanel() {
  const [view, setView] = useState<View>("lobby");
  const [showPlayerList, setShowPlayerList] = useState(false);
  const [showFriends, setShowFriends] = useState(false);
  const [showChangelog, setShowChangelog] = useState(false);
  const [showChat, setShowChat] = useState(false);
  const { mode: layoutMode, openSettings } = useLayoutSettings();
  const friendlyRoom = useNetStore((s) => s.friendlyRoom);
  const account = useNetStore((s) => s.account);
  const onlineCount = useNetStore((s) => s.onlineCount);
  const incomingFriendCount = useNetStore((s) => s.incomingFriendRequests.length);

  // 每个账号在当前浏览器首次进入新版本时自动展示更新日志。
  useEffect(() => {
    if (!account || !LATEST_CHANGELOG) return;
    try {
      const seenId = localStorage.getItem(changelogSeenKey(account));
      if (seenId !== LATEST_CHANGELOG.id) setShowChangelog(true);
    } catch {
      // 浏览器禁用本地存储时仍展示日志，但不阻断主页使用。
      setShowChangelog(true);
    }
  }, [account]);

  const closeChangelog = () => {
    if (account && LATEST_CHANGELOG) {
      try {
        localStorage.setItem(changelogSeenKey(account), LATEST_CHANGELOG.id);
      } catch {
        // 本地存储不可用时只关闭本次弹窗。
      }
    }
    setShowChangelog(false);
  };

  // 进入友谊战房间后，大厅整体切换为房间界面
  if (friendlyRoom) {
    return (
      <LayoutPreviewFrame mode={layoutMode}>
        <div className="h-full">
          <FriendlyRoomPanel />
          <InviteNotifyOverlay />
          <ChangelogModal open={showChangelog} onClose={closeChangelog} />
        </div>
      </LayoutPreviewFrame>
    );
  }

  const activeMobileView = view === "history" ? "profile" : view;
  const mobileNavItems: Array<{ view: Exclude<View, "history">; label: string }> = [
    { view: "lobby", label: "对战" },
    { view: "deck", label: "卡组" },
    { view: "catalog", label: "图鉴" },
    { view: "leaderboard", label: "排行" },
    { view: "profile", label: "我的" },
  ];

  return (
    <LayoutPreviewFrame mode={layoutMode}>
      <div
        className="flex h-full flex-col overflow-hidden bg-gray-950"
        style={{
          paddingTop: "env(safe-area-inset-top)",
          paddingLeft: "env(safe-area-inset-left)",
          paddingRight: "env(safe-area-inset-right)",
          paddingBottom: "env(safe-area-inset-bottom)",
        }}
      >
      <header className="flex h-16 shrink-0 items-center justify-between border-b border-gray-800 bg-gray-900/95 px-4 pr-16 @[1024px]:hidden">
        <div>
          <p className="text-base font-black tracking-tight text-white">GrandUMI</p>
          <p className="text-xs text-gray-500">海贼王卡牌对战</p>
        </div>
        <div className="flex items-center gap-1">
          <button
            type="button"
            onClick={() => setShowPlayerList(true)}
            aria-label={`查看 ${onlineCount} 名在线玩家`}
            className="flex h-11 min-w-11 items-center justify-center gap-1 rounded-xl px-2 text-sm text-gray-300 transition-colors hover:bg-gray-800 active:bg-gray-700"
          >
            <span className="h-2 w-2 rounded-full bg-green-400" />
            <span className="font-bold text-green-300">{onlineCount}</span>
          </button>
          <button
            type="button"
            onClick={() => setShowFriends(true)}
            aria-label={`打开好友中心${incomingFriendCount ? `，${incomingFriendCount} 条新申请` : ""}`}
            className="relative flex h-11 w-11 items-center justify-center rounded-xl text-gray-300 transition-colors hover:bg-gray-800 active:bg-gray-700"
          >
            <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <circle cx="9" cy="8" r="3" /><path d="M3.5 19c.6-3.5 2.4-5 5.5-5s4.9 1.5 5.5 5M16 8h5M18.5 5.5v5" />
            </svg>
            {incomingFriendCount > 0 && <span className="absolute right-1 top-1 min-w-4 rounded-full bg-red-500 px-1 text-[9px] font-black leading-4 text-white">{incomingFriendCount}</span>}
          </button>
          <button
            type="button"
            onClick={() => setShowChat(true)}
            aria-label="打开大厅聊天"
            className="flex h-11 w-11 items-center justify-center rounded-xl text-gray-300 transition-colors hover:bg-gray-800 active:bg-gray-700"
          >
            <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
              <path d="M5 18.5 3.8 21l3.8-1.3c1.3.6 2.8.9 4.4.9 5 0 9-3.5 9-8s-4-8-9-8-9 3.5-9 8c0 2.3 1 4.4 2 6Z" />
              <path d="M8 12h.01M12 12h.01M16 12h.01" />
            </svg>
          </button>
          <PlayerAvatar variant="header" />
        </div>
      </header>

      <div className="flex min-h-0 flex-1">
        {/* 桌面侧边导航 */}
        <nav className="hidden w-28 shrink-0 flex-col items-center gap-3 border-r border-gray-800 bg-gray-900 px-3 py-4 @[1024px]:flex">
          <PlayerAvatar />
          <button
            onClick={() => setView("lobby")}
            className={`h-10 w-full rounded-xl text-sm font-bold transition-colors ${view === "lobby" ? "bg-orange-500 text-white" : "text-gray-400 hover:bg-gray-800 hover:text-white"}`}
          >
            大厅
          </button>
          <button
            onClick={() => setView("deck")}
            className={`h-10 w-full rounded-xl text-sm font-bold transition-colors ${view === "deck" ? "bg-orange-500 text-white" : "text-gray-400 hover:bg-gray-800 hover:text-white"}`}
          >
            卡组
          </button>
          <button
            type="button"
            onClick={() => setShowFriends(true)}
            className="relative h-10 w-full rounded-xl text-sm font-bold text-gray-400 transition-colors hover:bg-gray-800 hover:text-white"
          >
            好友
            {incomingFriendCount > 0 && <span className="absolute right-1 top-1 min-w-4 rounded-full bg-red-500 px-1 text-[9px] leading-4 text-white">{incomingFriendCount}</span>}
          </button>
          <button
            onClick={() => setView("catalog")}
            className={`h-10 w-full rounded-xl text-sm font-bold transition-colors ${view === "catalog" ? "bg-orange-500 text-white" : "text-gray-400 hover:bg-gray-800 hover:text-white"}`}
          >
            卡牌图鉴
          </button>
          <button
            onClick={() => setView("leaderboard")}
            className={`h-10 w-full rounded-xl text-sm font-bold transition-colors ${view === "leaderboard" ? "bg-orange-500 text-white" : "text-gray-400 hover:bg-gray-800 hover:text-white"}`}
          >
            Leader榜
          </button>
          <button
            onClick={() => setView("profile")}
            className={`h-10 w-full rounded-xl text-sm font-bold transition-colors ${view === "profile" ? "bg-orange-500 text-white" : "text-gray-400 hover:bg-gray-800 hover:text-white"}`}
          >
            我的
          </button>
          <button
            onClick={() => setView("history")}
            className={`h-10 w-full rounded-xl text-sm font-bold transition-colors ${view === "history" ? "bg-orange-500 text-white" : "text-gray-400 hover:bg-gray-800 hover:text-white"}`}
          >
            对局记录
          </button>
          <div className="mt-auto flex w-full flex-col gap-2">
            <button
              type="button"
              onClick={openSettings}
              className="min-h-11 w-full rounded-lg border border-gray-800 bg-gray-950/60 px-2 py-2 text-xs text-gray-300 transition-colors hover:border-orange-500 hover:bg-gray-800 hover:text-white"
            >
              设置
            </button>
            <button
              type="button"
              onClick={() => setShowChangelog(true)}
              className="min-h-11 w-full rounded-lg border border-gray-800 bg-gray-950/60 px-2 py-2 text-xs text-gray-300 transition-colors hover:border-orange-500 hover:bg-gray-800 hover:text-white"
            >
              更新日志
            </button>
            <OnlineCountBadge onClick={() => setShowPlayerList(true)} />
            <NetStatePanel />
          </div>
        </nav>

        <main className="relative min-w-0 flex-1 overflow-hidden">
          {view === "lobby" && <LobbyPanel onGoToDeck={() => setView("deck")} />}
          {view === "deck" && <DeckChoosePanel onDeckSelected={() => setView("lobby")} />}
          {view === "catalog" && <CardCatalogPanel />}
          {view === "leaderboard" && <LeaderLeaderboardPanel />}
          {view === "history" && <HistoryPanel />}
          {view === "profile" && (
            <ProfilePanel
              profileEditor={<PlayerAvatar variant="profile" />}
              onOpenPlayers={() => setShowPlayerList(true)}
              onOpenHistory={() => setView("history")}
              onOpenChangelog={() => setShowChangelog(true)}
              onOpenSettings={openSettings}
            />
          )}
        </main>
      </div>

      <nav
        aria-label="主要导航"
        className="grid h-16 shrink-0 grid-cols-5 border-t border-gray-800 bg-gray-900/95 @[1024px]:hidden"
      >
        {mobileNavItems.map((item) => {
          const active = activeMobileView === item.view;
          return (
            <button
              key={item.view}
              type="button"
              onClick={() => setView(item.view)}
              aria-current={active ? "page" : undefined}
              className={`flex min-w-0 flex-col items-center justify-center gap-1 text-[11px] font-medium transition-colors ${
                active ? "text-orange-400" : "text-gray-500 hover:text-gray-200 active:bg-gray-800"
              }`}
            >
              <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                <MobileNavIcon view={item.view} />
              </svg>
              <span>{item.label}</span>
            </button>
          );
        })}
      </nav>

      <PlayerListPanel open={showPlayerList} onClose={() => setShowPlayerList(false)} />
      <FriendsPanel open={showFriends} onClose={() => setShowFriends(false)} />
      <Modal open={showChat} onClose={() => setShowChat(false)} title="大厅聊天" mobileSheet maxWidthClass="max-w-lg">
        <div className="h-[min(70cqh,36rem)] min-h-80 overflow-hidden rounded-xl border border-gray-800 bg-gray-950">
          <ChatPanel showHeader={false} />
        </div>
      </Modal>
      <InviteNotifyOverlay />
      <ChangelogModal open={showChangelog} onClose={closeChangelog} />
      </div>
    </LayoutPreviewFrame>
  );
}
