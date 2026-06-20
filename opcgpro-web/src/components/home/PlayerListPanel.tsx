"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { useNetStore } from "@/store/netStore";
import { useGameStore } from "@/store/gameStore";
import { HomeRequest } from "@/net/HomeProtocol";
import Modal from "@/components/ui/Modal";
import type { PlayerInfo } from "@/types/net";

const STATUS_LABEL: Record<PlayerInfo["status"], { text: string; cls: string }> = {
  idle:     { text: "空闲",   cls: "text-green-400" },
  matching: { text: "匹配中", cls: "text-yellow-400" },
  playing:  { text: "对战中", cls: "text-red-400" },
};

export default function PlayerListPanel({ open, onClose }: { open: boolean; onClose: () => void }) {
  const players = useNetStore((s) => s.playerList);
  const account = useNetStore((s) => s.account);
  const router = useRouter();

  // 打开时拉取一次，并每 4 秒刷新一次状态
  useEffect(() => {
    if (!open) return;
    HomeRequest.requestPlayerList();
    const t = setInterval(() => HomeRequest.requestPlayerList(), 4000);
    return () => clearInterval(t);
  }, [open]);

  const handleInvite = (p: PlayerInfo) => {
    HomeRequest.invitePlayer(p.account);
  };

  // 观战对战中玩家：发起观战 → 切观战模式 → 客户端路由进对战页（不断 WebSocket）
  const handleSpectate = (p: PlayerInfo) => {
    if (!p.roomId) return;
    HomeRequest.spectateRoom(p.roomId);
    useGameStore.getState().setMode("Observer");
    onClose();
    router.push("/game");
  };

  return (
    <Modal open={open} onClose={onClose} title="在线玩家">
      <div className="flex flex-col gap-1 w-80 max-h-96 overflow-y-auto">
        {players.length === 0 ? (
          <p className="text-gray-600 text-xs text-center py-8">暂无在线玩家</p>
        ) : (
          players.map((p) => {
            const isMe = p.account === account;
            const st = STATUS_LABEL[p.status] ?? STATUS_LABEL.idle;
            return (
              <div
                key={p.account}
                className="flex items-center gap-2 px-3 py-2 rounded-lg bg-gray-800/60 border border-gray-800"
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
                      className="px-3 py-1 rounded-lg text-xs font-bold transition-colors bg-purple-600 hover:bg-purple-500 text-white"
                    >
                      观战
                    </button>
                  ) : (
                    <button
                      onClick={() => handleInvite(p)}
                      disabled={p.status !== "idle"}
                      className={`px-3 py-1 rounded-lg text-xs font-bold transition-colors ${
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
      <p className="text-gray-500 text-[10px] text-center mt-2">
        邀请对方接受后，双方进入友谊战房间再选卡组
      </p>
    </Modal>
  );
}
