"use client";

import { useEffect, useMemo, useState } from "react";
import type { FormEvent } from "react";
import Modal from "@/components/ui/Modal";
import { HomeRequest } from "@/net/HomeProtocol";
import { LeaderChampionBadgeList } from "@/components/ui/LeaderChampionBadge";
import { useNetStore } from "@/store/netStore";
import type { FriendInfo, FriendPresenceStatus, FriendRequestInfo, FriendSearchPlayer } from "@/types/net";
import SpectateJoinButton from "./SpectateJoinButton";

type Tab = "friends" | "requests" | "search";

const STATUS_LABEL: Record<FriendPresenceStatus, { text: string; cls: string }> = {
  offline: { text: "离线", cls: "text-gray-500" },
  idle: { text: "在线 · 空闲", cls: "text-emerald-400" },
  matching: { text: "在线 · 匹配中", cls: "text-amber-400" },
  playing: { text: "在线 · 对战中", cls: "text-red-400" },
  spectating: { text: "在线 · 观战中", cls: "text-purple-400" },
};

function PlayerAvatar({ name, online }: { name: string; online: boolean }) {
  return (
    <div className="relative flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-orange-500/80 to-red-700/80 text-sm font-black text-white ring-1 ring-white/10">
      {name.trim().slice(0, 1).toLocaleUpperCase("zh-CN") || "?"}
      <span
        className={`absolute bottom-0 right-0 h-3 w-3 rounded-full border-2 border-gray-900 ${online ? "bg-emerald-400" : "bg-gray-600"}`}
        aria-hidden="true"
      />
    </div>
  );
}

function PlayerIdentity({ name, account, online, status, championLeaderNumbers }: {
  name: string;
  account: string;
  online: boolean;
  status?: FriendPresenceStatus;
  championLeaderNumbers?: string[];
}) {
  const statusView = STATUS_LABEL[status ?? (online ? "idle" : "offline")];
  return (
    <>
      <PlayerAvatar name={name} online={online} />
      <div className="min-w-0 flex-1">
        <p className="truncate text-sm font-bold text-white">{name}</p>
        <LeaderChampionBadgeList leaderNumbers={championLeaderNumbers} className="mt-1" />
        <p className="truncate text-[11px] text-gray-500">@{account}</p>
        <p className={`mt-0.5 text-[10px] ${statusView.cls}`}>{statusView.text}</p>
      </div>
    </>
  );
}

function EmptyState({ children }: { children: string }) {
  return (
    <div className="flex min-h-40 items-center justify-center rounded-2xl border border-dashed border-gray-800 bg-gray-950/50 px-6 text-center text-sm text-gray-600">
      {children}
    </div>
  );
}

export default function FriendsPanel({ open, onClose }: { open: boolean; onClose: () => void }) {
  const friends = useNetStore((state) => state.friends);
  const incoming = useNetStore((state) => state.incomingFriendRequests);
  const outgoing = useNetStore((state) => state.outgoingFriendRequests);
  const searchResults = useNetStore((state) => state.friendSearchResults);
  const [tab, setTab] = useState<Tab>("friends");
  const [query, setQuery] = useState("");
  const [hasSearched, setHasSearched] = useState(false);
  const [removeConfirm, setRemoveConfirm] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    HomeRequest.requestFriendList();
  }, [open]);

  const sortedFriends = useMemo(() => [...friends].sort((a, b) => {
    const onlineOrder = Number(b.online) - Number(a.online);
    return onlineOrder || a.name.localeCompare(b.name, "zh-CN");
  }), [friends]);

  const currentRelationships = useMemo(() => {
    const relationships = new Map<string, FriendSearchPlayer["relationship"]>();
    for (const friend of friends) relationships.set(friend.account, "friend");
    for (const request of incoming) relationships.set(request.account, "incoming");
    for (const request of outgoing) relationships.set(request.account, "outgoing");
    return relationships;
  }, [friends, incoming, outgoing]);

  const submitSearch = (event: FormEvent) => {
    event.preventDefault();
    const normalized = query.trim();
    if (!normalized) return;
    setHasSearched(true);
    HomeRequest.searchFriends(normalized);
  };

  const removeFriend = (friend: FriendInfo) => {
    if (removeConfirm !== friend.account) {
      setRemoveConfirm(friend.account);
      return;
    }
    HomeRequest.removeFriend(friend.account);
    setRemoveConfirm(null);
  };

  const switchTab = (next: Tab) => {
    setTab(next);
    setRemoveConfirm(null);
  };

  return (
    <Modal open={open} onClose={onClose} title="好友中心" mobileSheet maxWidthClass="max-w-2xl">
      <div className="flex h-[min(70cqh,36rem)] min-h-0 max-h-[calc(100cqh-7rem)] flex-col" data-testid="friends-panel">
        <div className="grid grid-cols-3 gap-1 rounded-xl bg-gray-950 p-1">
          <button type="button" onClick={() => switchTab("friends")} className={`min-h-11 rounded-lg text-sm font-bold transition-colors ${tab === "friends" ? "bg-orange-500 text-white" : "text-gray-500 hover:bg-gray-800 hover:text-gray-200"}`}>
            好友 {friends.length > 0 ? `(${friends.length})` : ""}
          </button>
          <button type="button" onClick={() => switchTab("requests")} className={`relative min-h-11 rounded-lg text-sm font-bold transition-colors ${tab === "requests" ? "bg-orange-500 text-white" : "text-gray-500 hover:bg-gray-800 hover:text-gray-200"}`}>
            申请
            {incoming.length > 0 && <span className="ml-1 rounded-full bg-red-500 px-1.5 py-0.5 text-[10px] text-white">{incoming.length}</span>}
          </button>
          <button type="button" onClick={() => switchTab("search")} className={`min-h-11 rounded-lg text-sm font-bold transition-colors ${tab === "search" ? "bg-orange-500 text-white" : "text-gray-500 hover:bg-gray-800 hover:text-gray-200"}`}>
            添加好友
          </button>
        </div>

        <div className="mt-3 min-h-0 flex-1 touch-pan-y overflow-y-auto overscroll-contain pr-1 [-webkit-overflow-scrolling:touch]">
          {tab === "friends" && (
            <div className="flex flex-col gap-2">
              {sortedFriends.length === 0 ? <EmptyState>还没有好友，去“添加好友”搜索账号或昵称吧</EmptyState> : sortedFriends.map((friend) => (
                <div key={friend.account} className="flex min-h-16 items-center gap-3 rounded-xl border border-gray-800 bg-gray-900/70 p-3">
                  <PlayerIdentity name={friend.name} account={friend.account} online={friend.online} status={friend.status} championLeaderNumbers={friend.championLeaderNumbers} />
                  {friend.status === "playing" && friend.roomId ? (
                    <SpectateJoinButton
                      roomId={friend.roomId}
                      seatIndex={friend.seatIndex ?? 0}
                      mode={friend.spectateMode}
                      isFriend
                    />
                  ) : (
                    <button
                      type="button"
                      onClick={() => HomeRequest.invitePlayer(friend.account)}
                      disabled={!friend.online || friend.status !== "idle"}
                      className="min-h-11 rounded-lg bg-orange-500 px-3 text-xs font-bold text-white transition-colors hover:bg-orange-400 disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-600"
                    >
                      邀请对战
                    </button>
                  )}
                  <button
                    type="button"
                    onClick={() => removeFriend(friend)}
                    className={`min-h-11 rounded-lg border px-3 text-xs font-bold transition-colors ${removeConfirm === friend.account ? "border-red-500 bg-red-950 text-red-300" : "border-gray-700 text-gray-500 hover:border-red-800 hover:text-red-400"}`}
                  >
                    {removeConfirm === friend.account ? "确认删除" : "删除"}
                  </button>
                </div>
              ))}
            </div>
          )}

          {tab === "requests" && (
            <div className="space-y-5">
              <section>
                <h3 className="mb-2 text-xs font-black uppercase tracking-wider text-gray-500">收到的申请 · {incoming.length}</h3>
                <div className="flex flex-col gap-2">
                  {incoming.length === 0 ? <EmptyState>暂无待处理的好友申请</EmptyState> : incoming.map((request: FriendRequestInfo) => (
                    <div key={request.id} className="flex min-h-16 items-center gap-3 rounded-xl border border-gray-800 bg-gray-900/70 p-3">
                      <PlayerIdentity name={request.name} account={request.account} online={request.online} />
                      <button type="button" onClick={() => HomeRequest.respondFriendRequest(request.id, true)} className="min-h-11 rounded-lg bg-emerald-600 px-3 text-xs font-bold text-white hover:bg-emerald-500">接受</button>
                      <button type="button" onClick={() => HomeRequest.respondFriendRequest(request.id, false)} className="min-h-11 rounded-lg border border-gray-700 px-3 text-xs font-bold text-gray-400 hover:border-red-800 hover:text-red-400">拒绝</button>
                    </div>
                  ))}
                </div>
              </section>
              <section>
                <h3 className="mb-2 text-xs font-black uppercase tracking-wider text-gray-500">已发送 · {outgoing.length}</h3>
                <div className="flex flex-col gap-2">
                  {outgoing.length === 0 ? <p className="rounded-xl bg-gray-950/50 px-3 py-5 text-center text-xs text-gray-600">没有等待回应的申请</p> : outgoing.map((request) => (
                    <div key={request.id} className="flex min-h-16 items-center gap-3 rounded-xl border border-gray-800 bg-gray-900/70 p-3">
                      <PlayerIdentity name={request.name} account={request.account} online={request.online} />
                      <span className="rounded-full bg-amber-950 px-2 py-1 text-[10px] font-bold text-amber-400">等待回应</span>
                      <button
                        type="button"
                        onClick={() => HomeRequest.cancelFriendRequest(request.id)}
                        className="min-h-11 rounded-lg border border-gray-700 px-3 text-xs font-bold text-gray-400 hover:border-red-800 hover:text-red-400"
                      >
                        撤回
                      </button>
                    </div>
                  ))}
                </div>
              </section>
            </div>
          )}

          {tab === "search" && (
            <div>
              <form onSubmit={submitSearch} className="flex gap-2">
                <input
                  type="search"
                  value={query}
                  onChange={(event) => {
                    setQuery(event.target.value);
                    setHasSearched(false);
                  }}
                  placeholder="输入账号或昵称"
                  aria-label="搜索好友账号或昵称"
                  maxLength={32}
                  className="h-12 min-w-0 flex-1 rounded-xl border border-gray-700 bg-gray-950 px-3 text-base text-white outline-none placeholder:text-gray-600 focus:border-orange-500"
                />
                <button type="submit" disabled={!query.trim()} className="h-12 rounded-xl bg-orange-500 px-5 text-sm font-black text-white hover:bg-orange-400 disabled:bg-gray-800 disabled:text-gray-600">
                  搜索
                </button>
              </form>
              <div className="mt-3 flex flex-col gap-2">
                {searchResults.map((player: FriendSearchPlayer) => {
                  const relationship = currentRelationships.get(player.account) ?? player.relationship;
                  return (
                    <div key={player.account} className="flex min-h-16 items-center gap-3 rounded-xl border border-gray-800 bg-gray-900/70 p-3">
                      <PlayerIdentity name={player.name} account={player.account} online={player.online} status={player.status} championLeaderNumbers={player.championLeaderNumbers} />
                      {relationship === "none" && <button type="button" onClick={() => HomeRequest.sendFriendRequest(player.account)} className="min-h-11 rounded-lg bg-orange-500 px-3 text-xs font-bold text-white hover:bg-orange-400">添加好友</button>}
                      {relationship === "friend" && <span className="text-xs font-bold text-emerald-400">已是好友</span>}
                      {relationship === "outgoing" && <span className="text-xs font-bold text-amber-400">已申请</span>}
                      {relationship === "incoming" && <button type="button" onClick={() => switchTab("requests")} className="min-h-11 rounded-lg bg-emerald-700 px-3 text-xs font-bold text-white hover:bg-emerald-600">去处理</button>}
                    </div>
                  );
                })}
                {hasSearched && searchResults.length === 0 && <EmptyState>没有找到匹配的玩家</EmptyState>}
              </div>
            </div>
          )}
        </div>
      </div>
    </Modal>
  );
}
