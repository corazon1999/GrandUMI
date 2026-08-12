"use client";

import { useState } from "react";
import { HomeRequest } from "@/net/HomeProtocol";
import { useNetStore } from "@/store/netStore";
import { showMessage } from "@/components/ui/MessageBox";

export default function MaintenanceControlPanel() {
  const maintenance = useNetStore((state) => state.maintenance);
  const connState = useNetStore((state) => state.connState);
  const [pending, setPending] = useState(false);
  const [confirming, setConfirming] = useState(false);

  if (!maintenance.canManage) {
    if (!maintenance.enabled) return null;
    return (
      <aside role="status" aria-label="维护更新中" className="z-40 shrink-0 border-b border-amber-600/60 bg-amber-950/40 px-4 py-3 text-center shadow-lg shadow-black/20">
        <p className="font-black text-amber-200">维护更新中</p>
        <p className="mt-1 text-xs leading-5 text-amber-100/70">排位、休闲匹配、好友房和单人对局已暂停；正在进行的对局不受影响。</p>
      </aside>
    );
  }

  const updateMaintenance = (enabled: boolean) => {
    setPending(true);
    setConfirming(false);
    if (!HomeRequest.setMaintenance(enabled)) {
      setPending(false);
      showMessage("服务器未连接，维护状态没有改变", "error");
      return;
    }
    window.setTimeout(() => setPending(false), 1_500);
  };

  return (
    <aside
      aria-label="维护控制面板"
      className="z-40 shrink-0 border-b border-amber-600/60 bg-gray-950 px-3 py-3 shadow-lg shadow-black/30 @[640px]:px-5"
    >
      <div className="mx-auto flex w-full max-w-3xl items-center gap-3 pr-14 @[640px]:pr-0">
        <div>
          <p className="text-xs font-bold uppercase tracking-[0.16em] text-amber-400">管理员 · 维护控制</p>
          <h2 className="mt-0.5 text-sm font-black text-white @[640px]:text-base">
            {maintenance.enabled ? "正在排空在线对局" : "游戏服务正常开放"}
          </h2>
        </div>
        <span className={`h-3 w-3 shrink-0 rounded-full ${maintenance.enabled ? "animate-pulse bg-amber-400" : "bg-emerald-400"}`} />

      {maintenance.enabled ? (
        <>
          <div className="ml-auto min-w-0 text-right" aria-live="polite">
            <p className="text-[11px] text-gray-400">进行中房间</p>
            <p className={`text-xl font-black leading-6 ${maintenance.activeRoomCount === 0 ? "text-emerald-400" : "text-amber-300"}`}>
              {maintenance.activeRoomCount}
              <span className="ml-1 text-sm font-bold text-gray-500">间</span>
            </p>
          </div>
          <button
            type="button"
            disabled={pending || connState !== "connected"}
            onClick={() => updateMaintenance(false)}
            className="ml-auto min-h-11 shrink-0 rounded-xl border border-emerald-600/70 bg-emerald-950/30 px-3 text-xs font-black text-emerald-300 transition-colors hover:bg-emerald-900/40 disabled:cursor-not-allowed disabled:opacity-50 @[480px]:px-4 @[480px]:text-sm"
          >
            结束维护
          </button>
        </>
      ) : confirming ? (
        <div className="ml-auto flex items-center gap-2">
          <button type="button" onClick={() => setConfirming(false)} className="min-h-11 rounded-xl bg-gray-800 px-3 text-sm font-bold text-gray-200">取消</button>
          <button type="button" disabled={pending} onClick={() => updateMaintenance(true)} className="min-h-11 rounded-xl bg-red-600 px-3 text-sm font-black text-white hover:bg-red-500 disabled:opacity-50">确认启动</button>
        </div>
      ) : (
        <button
          type="button"
          disabled={connState !== "connected"}
          onClick={() => setConfirming(true)}
          className="ml-auto min-h-11 shrink-0 rounded-xl bg-amber-500 px-4 text-sm font-black text-gray-950 transition-colors hover:bg-amber-400 disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-500"
        >
          启动维护
        </button>
      )}
      </div>
      {maintenance.enabled && maintenance.activeRoomCount === 0 && (
        <p className="mx-auto mt-2 w-full max-w-3xl text-xs font-bold text-emerald-300" role="status">全部对局已结束，可以开始正式服更新发布。</p>
      )}
      {confirming && (
        <p className="mx-auto mt-2 w-full max-w-3xl text-xs leading-5 text-red-200">确认后会停止新对局，并取消排队、邀请及尚未开局的房间。</p>
      )}
    </aside>
  );
}
