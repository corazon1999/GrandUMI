"use client";

import { useEffect, useState } from "react";
import { showMessage } from "@/components/ui/MessageBox";
import { HomeRequest } from "@/net/HomeProtocol";
import { useNetStore } from "@/store/netStore";
import RulesetControlPanel from "./RulesetControlPanel";

type AdminPanelProps = {
  onOpenCardBackReview: () => void;
  onOpenPlayers: () => void;
  onReturnToLobby: () => void;
};

function StatusCard({
  label,
  value,
  detail,
  tone,
}: {
  label: string;
  value: string | number;
  detail: string;
  tone: "emerald" | "amber" | "cyan" | "violet";
}) {
  const toneClasses = {
    emerald: "border-emerald-800/70 bg-emerald-950/20 text-emerald-300",
    amber: "border-amber-800/70 bg-amber-950/20 text-amber-300",
    cyan: "border-cyan-800/70 bg-cyan-950/20 text-cyan-300",
    violet: "border-violet-800/70 bg-violet-950/20 text-violet-300",
  }[tone];

  return (
    <article className={`min-w-0 rounded-2xl border p-4 ${toneClasses}`}>
      <p className="text-xs font-bold tracking-[0.12em] text-gray-400">{label}</p>
      <p className="mt-3 truncate text-2xl font-black text-white">{value}</p>
      <p className="mt-1 truncate text-xs">{detail}</p>
    </article>
  );
}

export default function AdminPanel({ onOpenCardBackReview, onOpenPlayers, onReturnToLobby }: AdminPanelProps) {
  const account = useNetStore((state) => state.account);
  const playerName = useNetStore((state) => state.playerName);
  const connState = useNetStore((state) => state.connState);
  const onlineCount = useNetStore((state) => state.onlineCount);
  const maintenance = useNetStore((state) => state.maintenance);
  const reviewQueue = useNetStore((state) => state.cardBackReviewQueue);
  const [announcement, setAnnouncement] = useState("");
  const [lastRefreshAt, setLastRefreshAt] = useState<Date | null>(null);

  const connected = connState === "connected";
  const pendingReviews = reviewQueue?.length;

  useEffect(() => {
    if (!connected || !maintenance.canManage) return;
    HomeRequest.requestMaintenanceState();
    HomeRequest.requestRulesetState();
    HomeRequest.requestCardBackReviewQueue();
    setLastRefreshAt(new Date());
  }, [connected, maintenance.canManage]);

  if (!maintenance.canManage) {
    return (
      <section className="flex h-full items-center justify-center overflow-y-auto p-4" data-testid="admin-panel-denied">
        <div className="w-full max-w-lg rounded-2xl border border-red-800/60 bg-red-950/20 px-5 py-12 text-center">
          <p className="text-xs font-bold tracking-[0.18em] text-red-300">ACCESS RESTRICTED</p>
          <h1 className="mt-2 text-2xl font-black text-white">当前账号没有管理权限</h1>
          <p className="mt-3 text-sm leading-6 text-gray-400">管理员权限由服务器校验。请确认已使用授权账号登录，并等待连接恢复。</p>
          <button
            type="button"
            onClick={onReturnToLobby}
            className="mt-6 min-h-11 rounded-xl bg-gray-800 px-5 text-sm font-bold text-white hover:bg-gray-700"
          >
            返回大厅
          </button>
        </div>
      </section>
    );
  }

  const refreshDashboard = () => {
    if (!connected) {
      showMessage("服务器未连接，暂时无法刷新管理状态", "error");
      return;
    }
    HomeRequest.requestMaintenanceState();
    HomeRequest.requestRulesetState();
    HomeRequest.requestCardBackReviewQueue();
    setLastRefreshAt(new Date());
  };

  const sendAnnouncement = () => {
    const content = announcement.trim();
    if (!content) {
      showMessage("请输入公告内容", "error");
      return;
    }
    if (!HomeRequest.sendGlobalAnnouncement(content)) {
      showMessage("服务器未连接，请稍后再试", "error");
    }
  };

  return (
    <section className="h-full overflow-y-auto bg-[radial-gradient(circle_at_top_right,rgba(245,158,11,0.08),transparent_34%)]" data-testid="admin-panel">
      <div className="mx-auto w-full max-w-6xl px-4 py-5 @[720px]:px-6 @[720px]:py-7">
        <header className="flex flex-col gap-4 @[640px]:flex-row @[640px]:items-end @[640px]:justify-between">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <p className="text-xs font-black tracking-[0.2em] text-amber-400">GRANDUMI · ADMIN CONSOLE</p>
              <span className="rounded-full border border-amber-800/70 bg-amber-950/40 px-2 py-0.5 text-[11px] font-bold text-amber-200">管理员</span>
            </div>
            <h1 className="mt-2 text-2xl font-black tracking-tight text-white @[640px]:text-3xl">管理中心</h1>
            <p className="mt-2 text-sm leading-6 text-gray-400">
              {playerName || account} · 集中查看服务状态、处理内容审核与发布全服通知。
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-xs text-gray-500" aria-live="polite">
              {lastRefreshAt ? `更新于 ${lastRefreshAt.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}` : "等待同步"}
            </span>
            <button
              type="button"
              onClick={refreshDashboard}
              disabled={!connected}
              className="min-h-11 rounded-xl border border-gray-700 bg-gray-900 px-4 text-sm font-bold text-gray-200 hover:border-gray-500 hover:bg-gray-800 disabled:cursor-not-allowed disabled:opacity-50"
            >
              刷新状态
            </button>
          </div>
        </header>

        <div className="mt-6 grid grid-cols-2 gap-3 @[720px]:grid-cols-4">
          <StatusCard
            label="服务连接"
            value={connected ? "已连接" : "未连接"}
            detail={!connected ? "等待服务器恢复连接" : maintenance.enabled ? "维护模式运行中" : "游戏服务正常开放"}
            tone={connected ? "emerald" : "amber"}
          />
          <StatusCard label="在线玩家" value={onlineCount} detail="当前已登录人数" tone="cyan" />
          <StatusCard
            label="进行中房间"
            value={maintenance.activeRoomCount}
            detail={maintenance.enabled ? "等待现有对局结束" : "实时对局数量"}
            tone="violet"
          />
          <StatusCard
            label="待审核卡背"
            value={pendingReviews ?? "—"}
            detail={pendingReviews === undefined ? "正在读取审核队列" : pendingReviews > 0 ? "有内容等待处理" : "审核队列已清空"}
            tone={pendingReviews ? "amber" : "emerald"}
          />
        </div>

        <div className="mt-6 grid gap-4 @[860px]:grid-cols-[minmax(0,1.35fr)_minmax(18rem,0.65fr)]">
          <section aria-label="全服公告" className="rounded-2xl border border-amber-800/70 bg-amber-950/20 p-4 @[640px]:p-5">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <p className="text-xs font-bold tracking-[0.16em] text-amber-400">即时通知</p>
                <h2 className="mt-1 text-lg font-black text-white">全服滚动公告</h2>
                <p className="mt-1 text-xs leading-5 text-gray-400">发送后立即在所有在线页面顶部展示，内容最多 200 字。</p>
              </div>
              <span className="rounded-full bg-gray-950/70 px-2.5 py-1 text-xs text-gray-500">{announcement.length}/200</span>
            </div>
            <textarea
              aria-label="公告内容"
              value={announcement}
              onChange={(event) => setAnnouncement(event.target.value)}
              placeholder="输入要发送给全服玩家的公告"
              maxLength={200}
              rows={4}
              className="mt-4 min-h-28 w-full resize-y rounded-xl border border-amber-900/80 bg-gray-950 px-3 py-3 text-sm leading-6 text-white outline-none placeholder:text-gray-600 focus:border-amber-400"
            />
            <div className="mt-3 flex flex-col gap-2 @[480px]:flex-row @[480px]:items-center @[480px]:justify-between">
              <p className="text-xs leading-5 text-amber-200/70">发送后保留输入内容，便于继续编辑或重复播报。</p>
              <button
                type="button"
                onClick={sendAnnouncement}
                disabled={!announcement.trim() || !connected}
                className="min-h-11 shrink-0 rounded-xl bg-amber-500 px-5 text-sm font-black text-gray-950 hover:bg-amber-400 disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-600"
              >
                发送全服公告
              </button>
            </div>
          </section>

          <section aria-label="常用管理入口" className="rounded-2xl border border-gray-800 bg-gray-900/70 p-4 @[640px]:p-5">
            <p className="text-xs font-bold tracking-[0.16em] text-gray-500">快捷入口</p>
            <h2 className="mt-1 text-lg font-black text-white">内容与玩家</h2>
            <div className="mt-4 grid gap-3">
              <button
                type="button"
                onClick={onOpenCardBackReview}
                className="flex min-h-14 items-center justify-between gap-3 rounded-xl border border-gray-800 bg-gray-950 px-4 text-left hover:border-amber-700 hover:bg-amber-950/20"
              >
                <span>
                  <span className="block text-sm font-black text-white">卡背审核</span>
                  <span className="mt-0.5 block text-xs text-gray-500">处理待公开的玩家投稿</span>
                </span>
                <span className="rounded-full bg-amber-500 px-2.5 py-1 text-xs font-black text-gray-950">{pendingReviews ?? "—"}</span>
              </button>
              <button
                type="button"
                onClick={onOpenPlayers}
                className="flex min-h-14 items-center justify-between gap-3 rounded-xl border border-gray-800 bg-gray-950 px-4 text-left hover:border-cyan-700 hover:bg-cyan-950/20"
              >
                <span>
                  <span className="block text-sm font-black text-white">在线玩家</span>
                  <span className="mt-0.5 block text-xs text-gray-500">查看玩家与当前对局状态</span>
                </span>
                <span className="text-sm font-black text-cyan-300">{onlineCount} 人</span>
              </button>
            </div>
          </section>
        </div>

        <div className="mt-6">
          <RulesetControlPanel />
        </div>

        <aside className="mt-4 rounded-xl border border-gray-800 bg-gray-950/70 px-4 py-3 text-xs leading-5 text-gray-500">
          权限与操作结果均由服务器再次校验。维护、版本切换与内容审核等操作不会仅依赖网页端判断。
        </aside>
      </div>
    </section>
  );
}
