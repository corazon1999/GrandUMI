"use client";

import { useNetStore } from "@/store/netStore";
import { HomeRequest } from "@/net/HomeProtocol";

/** 全局挂载于大厅：监听收到的对战邀请并弹窗，让玩家接受/拒绝（接受后进入友谊战房间） */
export default function InviteNotifyOverlay() {
  const invite    = useNetStore((s) => s.incomingInvite);
  const setInvite = useNetStore((s) => s.setIncomingInvite);

  if (!invite) return null;

  const accept = () => {
    HomeRequest.respondInvite(invite.inviteId, true);
    setInvite(null);
  };

  const decline = () => {
    HomeRequest.respondInvite(invite.inviteId, false);
    setInvite(null);
  };

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/60 backdrop-blur-sm">
      <div className="bg-gray-900 border border-orange-700 rounded-xl p-6 w-80 shadow-2xl">
        <p className="text-white text-base text-center mb-1">对战邀请</p>
        <p className="text-orange-400 text-sm text-center font-bold mb-1 truncate">{invite.fromName}</p>
        <p className="text-gray-400 text-xs text-center mb-5">邀请你进入友谊战房间</p>
        <div className="flex gap-2">
          <button
            onClick={decline}
            className="flex-1 py-2 rounded-lg bg-gray-800 text-gray-300 text-sm hover:bg-gray-700 transition-colors"
          >
            拒绝
          </button>
          <button
            onClick={accept}
            className="flex-1 py-2 rounded-lg bg-orange-500 text-white text-sm font-bold hover:bg-orange-400 transition-colors"
          >
            接受
          </button>
        </div>
      </div>
    </div>
  );
}
