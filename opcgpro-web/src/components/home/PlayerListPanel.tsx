"use client";

import { useEffect, useMemo, useState } from "react";
import { useNetStore } from "@/store/netStore";
import { HomeRequest } from "@/net/HomeProtocol";
import Modal from "@/components/ui/Modal";
import type { PlayerInfo } from "@/types/net";

const STATUS_LABEL: Record<PlayerInfo["status"], { text: string; cls: string }> = {
  idle:     { text: "空闲",   cls: "text-green-400" },
  matching: { text: "匹配中", cls: "text-yellow-400" },
  playing:  { text: "对战中", cls: "text-red-400" },
  spectating:{ text: "观战中", cls: "text-purple-400" },
};

export default function PlayerListPanel({ open, onClose }: { open: boolean; onClose: () => void }) {
  const players = useNetStore((s) => s.playerList);
  const account = useNetStore((s) => s.account);
  const friends = useNetStore((s) => s.friends);
  const incomingRequests = useNetStore((s) => s.incomingFriendRequests);
  const outgoingRequests = useNetStore((s) => s.outgoingFriendRequests);
  const spectateState = useNetStore((s) => s.spectateState);
  const spectateRoomId = useNetStore((s) => s.spectateRoomId);
  const [search, setSearch] = useState("");

  // 打开时拉取一页。在线人数由服务端主动推送，避免大量客户端轮询形成平方级流量。
  useEffect(() => {
    if (!open) return;
    HomeRequest.requestPlayerList(0, 200);
  }, [open]);

  const visiblePlayers = useMemo(() => {
    const keyword = search.trim().toLocaleLowerCase("zh-CN");
    return players
      .filter((player) => {
        if (!keyword) return true;
        return player.name.toLocaleLowerCase("zh-CN").includes(keyword)
          || player.account.toLocaleLowerCase("zh-CN").includes(keyword);
      })
      .map((player, index) => ({ player, index }))
      .sort((a, b) => {
        const playingOrder = Number(b.player.status === "playing")
          - Number(a.player.status === "playing");
        return playingOrder || a.index - b.index;
      })
      .map(({ player }) => player);
  }, [players, search]);

  const relationships = useMemo(() => {
    const result = new Map<string, { kind: "friend" | "incoming" | "outgoing"; requestId?: number }>();
    for (const friend of friends) result.set(friend.account.toLocaleLowerCase("zh-CN"), { kind: "friend" });
    for (const request of incomingRequests) result.set(request.account.toLocaleLowerCase("zh-CN"), { kind: "incoming", requestId: request.id });
    for (const request of outgoingRequests) result.set(request.account.toLocaleLowerCase("zh-CN"), { kind: "outgoing", requestId: request.id });
    return result;
  }, [friends, incomingRequests, outgoingRequests]);

  const handleInvite = (p: PlayerInfo) => {
    HomeRequest.invitePlayer(p.account);
  };

  // 等服务端确认房间有效后再进入对战页，失败时保留弹窗并显示原因
  const handleSpectate = (p: PlayerInfo) => {
    if (!p.roomId) return;
    HomeRequest.spectateRoom(p.roomId, p.seatIndex ?? 0);
  };

  const handleClose = () => {
    setSearch("");
    onClose();
  };

  return (
    <Modal open={open} onClose={handleClose} title="在线玩家" mobileSheet maxWidthClass="max-w-md">
      <div className="w-full">
        <input
          type="search"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          placeholder="搜索玩家名称或账号"
          aria-label="搜索在线玩家"
          className="h-12 w-full rounded-xl border border-gray-700 bg-gray-950 px-3 text-base text-white outline-none placeholder:text-gray-600 focus:border-orange-500"
        />
        <div className="mt-2 flex max-h-[55cqh] flex-col gap-1 overflow-y-auto @[1024px]:max-h-96">
          {players.length === 0 ? (
            <p className="py-8 text-center text-xs text-gray-600">暂无在线玩家</p>
          ) : visiblePlayers.length === 0 ? (
            <p className="py-8 text-center text-xs text-gray-600">未找到匹配的在线玩家</p>
          ) : (
            visiblePlayers.map((p) => {
              const isMe = p.account === account;
              const st = STATUS_LABEL[p.status] ?? STATUS_LABEL.idle;
              const relationship = relationships.get(p.account.toLocaleLowerCase("zh-CN"));
              return (
                <div
                  key={p.account}
                  className="flex min-h-14 items-center gap-2 rounded-xl border border-gray-800 bg-gray-800/60 px-3 py-2"
                >
                  <div className="flex-1 min-w-0">
                    <p className="text-white text-sm font-medium truncate">
                      {p.name}
                      {isMe && <span className="text-orange-400 text-[10px] ml-1">（我）</span>}
                    </p>
                    <p className="truncate text-[10px] text-gray-500">@{p.account}</p>
                    <p className={`text-[10px] ${st.cls}`}>{st.text}</p>
                  </div>
                  {!isMe && (
                    <div className="flex max-w-48 flex-wrap justify-end gap-1">
                      {!relationship && (
                        <button
                          type="button"
                          onClick={() => HomeRequest.sendFriendRequest(p.account)}
                          className="min-h-11 rounded-lg bg-sky-700 px-3 text-xs font-bold text-white transition-colors hover:bg-sky-600"
                        >
                          添加好友
                        </button>
                      )}
                      {relationship?.kind === "friend" && (
                        <span
                          className="flex min-h-11 min-w-11 items-center justify-center text-emerald-400"
                          aria-label="已是好友"
                          title="已是好友"
                        >
                          <svg
                            viewBox="0 0 24 24"
                            className="h-6 w-6"
                            fill="none"
                            stroke="currentColor"
                            strokeWidth="1.8"
                            strokeLinecap="round"
                            strokeLinejoin="round"
                            aria-hidden="true"
                          >
                            <circle cx="8.5" cy="8" r="3" />
                            <path d="M3.5 19c.6-3.5 2.3-5 5-5 1.7 0 3 .5 3.9 1.5" />
                            <path d="m14.5 17 2 2 4-5" />
                          </svg>
                        </span>
                      )}
                      {relationship?.kind === "outgoing" && <span className="flex min-h-11 items-center px-2 text-xs font-bold text-amber-400">已申请</span>}
                      {relationship?.kind === "incoming" && relationship.requestId && (
                        <button
                          type="button"
                          onClick={() => HomeRequest.respondFriendRequest(relationship.requestId!, true)}
                          className="min-h-11 rounded-lg bg-emerald-700 px-3 text-xs font-bold text-white transition-colors hover:bg-emerald-600"
                        >
                          接受好友
                        </button>
                      )}
                      {p.status === "playing" && p.roomId ? (
                        <button
                          type="button"
                          onClick={() => handleSpectate(p)}
                          disabled={spectateState === "joining"}
                          className="min-h-11 rounded-lg bg-purple-600 px-3 text-sm font-bold text-white transition-colors hover:bg-purple-500 disabled:cursor-wait disabled:bg-purple-950 disabled:text-purple-300"
                        >
                          {spectateState === "joining" && spectateRoomId === p.roomId ? "进入中…" : "观战"}
                        </button>
                      ) : (
                        <button
                          type="button"
                          onClick={() => handleInvite(p)}
                          disabled={p.status !== "idle"}
                          className={`min-h-11 rounded-lg px-3 text-sm font-bold transition-colors ${
                            p.status === "idle"
                              ? "bg-orange-500 hover:bg-orange-400 text-white"
                              : "bg-gray-700 text-gray-500 cursor-not-allowed"
                          }`}
                        >
                          邀请对战
                        </button>
                      )}
                    </div>
                  )}
                </div>
              );
            })
          )}
        </div>
      </div>
      <p className="mt-3 text-center text-sm leading-5 text-gray-500">
        可以直接添加好友；邀请对方接受后，双方进入友谊战房间再选卡组
      </p>
    </Modal>
  );
}
