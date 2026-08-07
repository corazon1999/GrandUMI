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
    <div className="fixed inset-0 z-[60] flex items-end justify-center bg-black/60 px-0 backdrop-blur-sm @[640px]:items-center @[640px]:p-4">
      <div className="w-full max-w-sm rounded-t-2xl border border-b-0 border-orange-700 bg-gray-900 p-5 pb-[calc(1.25rem+env(safe-area-inset-bottom))] shadow-2xl @[640px]:rounded-xl @[640px]:border-b @[640px]:p-6">
        <p className="text-white text-base text-center mb-1">对战邀请</p>
        <p className="text-orange-400 text-sm text-center font-bold mb-1 truncate">{invite.fromName}</p>
        <p className="text-gray-400 text-xs text-center mb-5">邀请你进入友谊战房间</p>
        <div className="flex gap-2">
          <button
            onClick={decline}
            className="min-h-12 flex-1 rounded-xl bg-gray-800 text-sm text-gray-300 transition-colors hover:bg-gray-700"
          >
            拒绝
          </button>
          <button
            onClick={accept}
            className="min-h-12 flex-1 rounded-xl bg-orange-500 text-sm font-bold text-white transition-colors hover:bg-orange-400"
          >
            接受
          </button>
        </div>
      </div>
    </div>
  );
}
