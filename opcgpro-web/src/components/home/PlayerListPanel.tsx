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
  const spectateState = useNetStore((s) => s.spectateState);
  const spectateRoomId = useNetStore((s) => s.spectateRoomId);
  const [search, setSearch] = useState("");

  // 打开时拉取一次，并每 4 秒刷新一次状态
  useEffect(() => {
    if (!open) return;
    HomeRequest.requestPlayerList();
    const t = setInterval(() => HomeRequest.requestPlayerList(), 4000);
    return () => clearInterval(t);
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

  const handleInvite = (p: PlayerInfo) => {
    HomeRequest.invitePlayer(p.account);
  };

  // 等服务端确认房间有效后再进入对战页，失败时保留弹窗并显示原因
  const handleSpectate = (p: PlayerInfo) => {
    if (!p.roomId) return;
    HomeRequest.spectateRoom(p.roomId);
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
        <div className="mt-2 flex max-h-[55dvh] flex-col gap-1 overflow-y-auto lg:max-h-96">
          {players.length === 0 ? (
            <p className="py-8 text-center text-xs text-gray-600">暂无在线玩家</p>
          ) : visiblePlayers.length === 0 ? (
            <p className="py-8 text-center text-xs text-gray-600">未找到匹配的在线玩家</p>
          ) : (
            visiblePlayers.map((p) => {
              const isMe = p.account === account;
              const st = STATUS_LABEL[p.status] ?? STATUS_LABEL.idle;
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
                    <p className={`text-[10px] ${st.cls}`}>{st.text}</p>
                  </div>
                  {!isMe && (
                    p.status === "playing" && p.roomId ? (
                      <button
                        onClick={() => handleSpectate(p)}
                        disabled={spectateState === "joining"}
                        className="min-h-11 rounded-lg bg-purple-600 px-3 text-sm font-bold text-white transition-colors hover:bg-purple-500 disabled:cursor-wait disabled:bg-purple-950 disabled:text-purple-300"
                      >
                        {spectateState === "joining" && spectateRoomId === p.roomId ? "进入中…" : "观战"}
                      </button>
                    ) : (
                      <button
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
                    )
                  )}
                </div>
              );
            })
          )}
        </div>
      </div>
      <p className="mt-3 text-center text-sm leading-5 text-gray-500">
        邀请对方接受后，双方进入友谊战房间再选卡组
      </p>
    </Modal>
  );
}
