"use client";

import { useEffect, useMemo, useState } from "react";
import { showMessage } from "@/components/ui/MessageBox";
import { HomeRequest } from "@/net/HomeProtocol";
import { useNetStore } from "@/store/netStore";
import type {
  AdminDeploymentEnvironment,
  AdminDeploymentStatus,
  DailyMatchCountPoint,
  OnlinePlayerPeakPoint,
} from "@/types/net";
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
  onClick,
  expanded,
  controls,
}: {
  label: string;
  value: string | number;
  detail: string;
  tone: "emerald" | "amber" | "cyan" | "violet";
  onClick?: () => void;
  expanded?: boolean;
  controls?: string;
}) {
  const toneClasses = {
    emerald: "border-emerald-800/70 bg-emerald-950/20 text-emerald-300",
    amber: "border-amber-800/70 bg-amber-950/20 text-amber-300",
    cyan: "border-cyan-800/70 bg-cyan-950/20 text-cyan-300",
    violet: "border-violet-800/70 bg-violet-950/20 text-violet-300",
  }[tone];

  const content = (
    <>
      <p className="text-xs font-bold tracking-[0.12em] text-gray-400">{label}</p>
      <p className="mt-3 truncate text-2xl font-black text-white">{value}</p>
      <p className="mt-1 text-left text-xs">{detail}</p>
    </>
  );
  if (onClick) {
    return (
      <button
        type="button"
        onClick={onClick}
        aria-expanded={expanded}
        aria-controls={controls}
        className={`min-h-28 min-w-0 rounded-2xl border p-4 text-left transition hover:-translate-y-0.5 hover:border-cyan-400 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-cyan-300 ${toneClasses}`}
      >
        {content}
      </button>
    );
  }
  return <article className={`min-w-0 rounded-2xl border p-4 ${toneClasses}`}>{content}</article>;
}

function MatchChart({ points }: { points: DailyMatchCountPoint[] }) {
  const chart = useMemo(() => {
    const width = 720;
    const height = 220;
    const paddingX = 32;
    const paddingY = 24;
    const displayMax = Math.max(0, ...points.map((point) => point.count));
    const maxCount = Math.max(1, displayMax);
    const step = points.length > 1 ? (width - paddingX * 2) / (points.length - 1) : 0;
    const coordinates = points.map((point, index) => ({
      ...point,
      x: paddingX + step * index,
      y: height - paddingY - (point.count / maxCount) * (height - paddingY * 2),
    }));
    return {
      width,
      height,
      displayMax,
      coordinates,
      line: coordinates.map((point) => `${point.x},${point.y}`).join(" "),
      area: coordinates.length
        ? `${paddingX},${height - paddingY} ${coordinates.map((point) => `${point.x},${point.y}`).join(" ")} ${width - paddingX},${height - paddingY}`
        : "",
    };
  }, [points]);

  if (!points.length) {
    return <div className="flex min-h-48 items-center justify-center text-sm text-gray-500">场次数据正在初始化</div>;
  }

  return (
    <div>
      <svg viewBox={`0 0 ${chart.width} ${chart.height}`} className="h-auto min-h-48 w-full overflow-visible" role="img" aria-label={`每日完成场次折线图，最高 ${chart.displayMax} 场`}>
        {[0, 0.5, 1].map((ratio) => {
          const y = 24 + ratio * 172;
          return <line key={ratio} x1="32" x2="688" y1={y} y2={y} stroke="rgb(31 41 55)" strokeWidth="1" />;
        })}
        <polygon points={chart.area} fill="rgba(139,92,246,0.14)" />
        <polyline points={chart.line} fill="none" stroke="rgb(167 139 250)" strokeWidth="4" strokeLinejoin="round" strokeLinecap="round" />
        {chart.coordinates.map((point) => (
          <circle key={point.date} cx={point.x} cy={point.y} r="5" fill="rgb(109 40 217)" stroke="rgb(221 214 254)" strokeWidth="2">
            <title>{`${point.date}：${point.count} 场`}</title>
          </circle>
        ))}
      </svg>
      <div className="mt-2 flex justify-between text-[11px] text-gray-500">
        <span>{points[0]?.date.slice(5)}</span>
        <span>最高 {chart.displayMax} 场</span>
        <span>{points.at(-1)?.date.slice(5)}</span>
      </div>
      <table className="sr-only">
        <caption>每日完成场次</caption>
        <thead><tr><th>日期</th><th>完成场次</th></tr></thead>
        <tbody>{points.map((point) => <tr key={point.date}><td>{point.date}</td><td>{point.count}</td></tr>)}</tbody>
      </table>
    </div>
  );
}

function formatBytes(bytes: number) {
  if (!Number.isFinite(bytes) || bytes <= 0) return "—";
  return `${(bytes / 1024 ** 3).toFixed(bytes >= 100 * 1024 ** 3 ? 0 : 1)} GB`;
}

function formatTimestamp(timestamp?: number | null) {
  if (!timestamp) return "等待首次采样";
  return new Date(timestamp).toLocaleString([], { month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" });
}

function PeakChart({ points }: { points: OnlinePlayerPeakPoint[] }) {
  const chart = useMemo(() => {
    const width = 720;
    const height = 220;
    const paddingX = 32;
    const paddingY = 24;
    const displayMax = Math.max(0, ...points.map((point) => point.peak));
    const maxPeak = Math.max(1, displayMax);
    const step = points.length > 1 ? (width - paddingX * 2) / (points.length - 1) : 0;
    const coordinates = points.map((point, index) => ({
      ...point,
      x: paddingX + step * index,
      y: height - paddingY - (point.peak / maxPeak) * (height - paddingY * 2),
    }));
    return {
      width,
      height,
      maxPeak,
      displayMax,
      coordinates,
      line: coordinates.map((point) => `${point.x},${point.y}`).join(" "),
      area: coordinates.length
        ? `${paddingX},${height - paddingY} ${coordinates.map((point) => `${point.x},${point.y}`).join(" ")} ${width - paddingX},${height - paddingY}`
        : "",
    };
  }, [points]);

  if (!points.length) {
    return <div className="flex min-h-48 items-center justify-center text-sm text-gray-500">峰值数据正在初始化</div>;
  }

  return (
    <div>
      <svg
        viewBox={`0 0 ${chart.width} ${chart.height}`}
        className="h-auto min-h-48 w-full overflow-visible"
        role="img"
        aria-label={`每日在线玩家峰值折线图，最高 ${chart.displayMax} 人`}
      >
        {[0, 0.5, 1].map((ratio) => {
          const y = 24 + ratio * 172;
          return <line key={ratio} x1="32" x2="688" y1={y} y2={y} stroke="rgb(31 41 55)" strokeWidth="1" />;
        })}
        <polygon points={chart.area} fill="rgba(34,211,238,0.12)" />
        <polyline points={chart.line} fill="none" stroke="rgb(34 211 238)" strokeWidth="4" strokeLinejoin="round" strokeLinecap="round" />
        {chart.coordinates.map((point) => (
          <g key={point.date}>
            <circle cx={point.x} cy={point.y} r="5" fill="rgb(8 145 178)" stroke="rgb(165 243 252)" strokeWidth="2">
              <title>{`${point.date}：${point.peak} 人`}</title>
            </circle>
          </g>
        ))}
      </svg>
      <div className="mt-2 flex justify-between text-[11px] text-gray-500">
        <span>{points[0]?.date.slice(5)}</span>
        <span>最高 {chart.displayMax} 人</span>
        <span>{points.at(-1)?.date.slice(5)}</span>
      </div>
      <table className="sr-only">
        <caption>每日在线玩家峰值</caption>
        <thead><tr><th>日期</th><th>峰值人数</th></tr></thead>
        <tbody>{points.map((point) => <tr key={point.date}><td>{point.date}</td><td>{point.peak}</td></tr>)}</tbody>
      </table>
    </div>
  );
}

const deploymentLabels: Record<AdminDeploymentStatus["state"], string> = {
  idle: "待命",
  queued: "已排队",
  running: "发布中",
  success: "已完成",
  failed: "失败",
  unavailable: "未配置",
};

function shortCommit(commit?: string | null) {
  return commit ? commit.slice(0, 12) : "—";
}

export default function AdminPanel({ onOpenCardBackReview, onOpenPlayers, onReturnToLobby }: AdminPanelProps) {
  const account = useNetStore((state) => state.account);
  const playerName = useNetStore((state) => state.playerName);
  const connState = useNetStore((state) => state.connState);
  const maintenance = useNetStore((state) => state.maintenance);
  const reviewQueue = useNetStore((state) => state.cardBackReviewQueue);
  const adminOperations = useNetStore((state) => state.adminOperations);
  const adminPlayerSearchResults = useNetStore((state) => state.adminPlayerSearchResults);
  const adminTemporaryPassword = useNetStore((state) => state.adminTemporaryPassword);
  const [announcement, setAnnouncement] = useState("");
  const [lastRefreshAt, setLastRefreshAt] = useState<Date | null>(null);
  const [showPeakChart, setShowPeakChart] = useState(false);
  const [peakRange, setPeakRange] = useState<7 | 30>(7);
  const [showMatchChart, setShowMatchChart] = useState(false);
  const [matchRange, setMatchRange] = useState<7 | 30>(7);
  const [playerQuery, setPlayerQuery] = useState("");
  const [selectedPlayerAccount, setSelectedPlayerAccount] = useState<string | null>(null);
  const [renameValue, setRenameValue] = useState("");

  const connected = connState === "connected";
  const pendingReviews = reviewQueue?.length;
  const authoritativeOnlineCount = adminOperations.onlineCount ?? "—";
  const todayMatches = adminOperations.matches7.at(-1)?.count ?? "—";
  const storage = adminOperations.storage;
  const usedStoragePercent = storage?.totalBytes
    ? Math.max(0, Math.min(100, ((storage.totalBytes - storage.availableBytes) / storage.totalBytes) * 100))
    : null;
  const selectedPlayer = adminPlayerSearchResults.find((player) => player.account === selectedPlayerAccount) ?? null;

  useEffect(() => {
    if (!connected || !maintenance.canManage) return;
    HomeRequest.requestMaintenanceState();
    HomeRequest.requestRulesetState();
    HomeRequest.requestCardBackReviewQueue();
    HomeRequest.requestAdminOperations();
    setLastRefreshAt(new Date());
  }, [connected, maintenance.canManage]);

  useEffect(() => {
    if (!connected || !maintenance.canManage) return;
    const timer = window.setInterval(() => HomeRequest.requestAdminOperations(), 5_000);
    return () => window.clearInterval(timer);
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
    HomeRequest.requestAdminOperations();
    setLastRefreshAt(new Date());
  };

  const deployLatest = (environment: AdminDeploymentEnvironment) => {
    const production = environment === "production";
    const confirmed = window.confirm(production
      ? "确认将远端 main 最新版本发布到正式服？系统会再次检查：该提交已部署测试服、待发布日志已归档、当前没有进行中房间。"
      : "确认将远端 main 最新版本部署到测试服？发布期间测试服会短暂重启。");
    if (!confirmed) return;
    if (!HomeRequest.deployLatest(environment)) showMessage("服务器未连接，无法提交发布任务", "error");
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

  const searchPlayers = () => {
    const query = playerQuery.trim();
    if (!query) {
      showMessage("请输入玩家账号或昵称", "error");
      return;
    }
    setSelectedPlayerAccount(null);
    setRenameValue("");
    HomeRequest.searchAdminPlayers(query);
  };

  const selectPlayer = (target: (typeof adminPlayerSearchResults)[number]) => {
    setSelectedPlayerAccount(target.account);
    setRenameValue(target.displayName);
    useNetStore.getState().setAdminTemporaryPassword(null);
  };

  const renamePlayer = () => {
    const nextName = renameValue.trim();
    if (!selectedPlayer || !nextName) return;
    if (nextName === selectedPlayer.displayName) {
      showMessage("新昵称与当前昵称相同", "error");
      return;
    }
    if (!window.confirm(`确认将账号“${selectedPlayer.account}”的昵称改为“${nextName}”？`)) return;
    HomeRequest.renameAdminPlayer(selectedPlayer.account, nextName);
  };

  const resetPlayerPassword = () => {
    if (!selectedPlayer) return;
    if (!window.confirm(`确认重置账号“${selectedPlayer.account}”的密码？该玩家所有登录会话会立即失效。`)) return;
    HomeRequest.resetAdminPlayerPassword(selectedPlayer.account);
  };

  const copyTemporaryPassword = async () => {
    if (!adminTemporaryPassword) return;
    try {
      await navigator.clipboard.writeText(adminTemporaryPassword.password);
      showMessage("临时密码已复制", "info");
    } catch {
      showMessage("复制失败，请手动选择临时密码", "error");
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

        <div className="mt-6 grid grid-cols-2 gap-3 @[720px]:grid-cols-3 @[1040px]:grid-cols-6">
          <StatusCard
            label="服务连接"
            value={connected ? "已连接" : "未连接"}
            detail={!connected ? "等待服务器恢复连接" : maintenance.enabled ? "维护模式运行中" : "游戏服务正常开放"}
            tone={connected ? "emerald" : "amber"}
          />
          <StatusCard
            label="在线玩家"
            value={authoritativeOnlineCount}
            detail={showPeakChart ? "点击收起峰值趋势" : "点击查看近一周/月峰值"}
            tone="cyan"
            onClick={() => setShowPeakChart((visible) => !visible)}
            expanded={showPeakChart}
            controls="online-peak-panel"
          />
          <StatusCard
            label="今日完成场次"
            value={todayMatches}
            detail={showMatchChart ? "点击收起场次趋势" : "点击查看近一周/月场次"}
            tone="violet"
            onClick={() => setShowMatchChart((visible) => !visible)}
            expanded={showMatchChart}
            controls="daily-match-panel"
          />
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
          <StatusCard
            label="磁盘可用空间"
            value={storage ? formatBytes(storage.availableBytes) : "—"}
            detail={usedStoragePercent === null ? "等待低频采样" : `已用 ${usedStoragePercent.toFixed(1)}% · ${storage?.refreshIntervalHours ?? 3} 小时缓存`}
            tone={storage?.healthy === false ? "amber" : "emerald"}
          />
        </div>

        {showPeakChart && (
          <section id="online-peak-panel" aria-label="在线玩家峰值" className="mt-4 rounded-2xl border border-cyan-900/70 bg-cyan-950/15 p-4 @[640px]:p-5">
            <div className="flex flex-col gap-3 @[520px]:flex-row @[520px]:items-center @[520px]:justify-between">
              <div>
                <p className="text-xs font-bold tracking-[0.16em] text-cyan-400">PLAYER TRAFFIC</p>
                <h2 className="mt-1 text-lg font-black text-white">每日在线玩家峰值</h2>
                <p className="mt-1 text-xs leading-5 text-gray-400">按 UTC+8 自然日统计；当天数据会随在线人数增长实时更新。</p>
              </div>
              <div className="grid grid-cols-2 rounded-xl border border-gray-800 bg-gray-950 p-1" aria-label="统计周期">
                {([7, 30] as const).map((range) => (
                  <button
                    key={range}
                    type="button"
                    onClick={() => setPeakRange(range)}
                    aria-pressed={peakRange === range}
                    className={`min-h-11 rounded-lg px-4 text-sm font-bold ${peakRange === range ? "bg-cyan-500 text-gray-950" : "text-gray-400 hover:text-white"}`}
                  >
                    近{range === 7 ? "一周" : "一月"}
                  </button>
                ))}
              </div>
            </div>
            <div className="mt-4 overflow-x-auto">
              <div className="min-w-[34rem]"><PeakChart points={peakRange === 7 ? adminOperations.peaks7 : adminOperations.peaks30} /></div>
            </div>
          </section>
        )}

        {showMatchChart && (
          <section id="daily-match-panel" aria-label="每日完成场次" className="mt-4 rounded-2xl border border-violet-900/70 bg-violet-950/15 p-4 @[640px]:p-5">
            <div className="flex flex-col gap-3 @[520px]:flex-row @[520px]:items-center @[520px]:justify-between">
              <div>
                <p className="text-xs font-bold tracking-[0.16em] text-violet-400">MATCH TRAFFIC</p>
                <h2 className="mt-1 text-lg font-black text-white">每日完成场次</h2>
                <p className="mt-1 text-xs leading-5 text-gray-400">按 UTC+8 自然日统计已完成的真人对局，不包含机器人和同账号测试局；服务端每 10 分钟更新缓存。</p>
              </div>
              <div className="grid grid-cols-2 rounded-xl border border-gray-800 bg-gray-950 p-1" aria-label="场次统计周期">
                {([7, 30] as const).map((range) => (
                  <button key={range} type="button" onClick={() => setMatchRange(range)} aria-pressed={matchRange === range} className={`min-h-11 rounded-lg px-4 text-sm font-bold ${matchRange === range ? "bg-violet-500 text-white" : "text-gray-400 hover:text-white"}`}>
                    近{range === 7 ? "一周" : "一月"}
                  </button>
                ))}
              </div>
            </div>
            <div className="mt-4 overflow-x-auto"><div className="min-w-[34rem]"><MatchChart points={matchRange === 7 ? adminOperations.matches7 : adminOperations.matches30} /></div></div>
            <p className="mt-2 text-[11px] text-gray-500">统计缓存更新于 {formatTimestamp(adminOperations.matchesUpdatedAt)}</p>
          </section>
        )}

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
                <span className="text-sm font-black text-cyan-300">
                  {typeof authoritativeOnlineCount === "number" ? `${authoritativeOnlineCount} 人` : "暂无数据"}
                </span>
              </button>
            </div>
          </section>
        </div>

        <div className="mt-6 grid gap-4 @[900px]:grid-cols-[minmax(16rem,0.65fr)_minmax(0,1.35fr)]">
          <section aria-label="服务器磁盘空间" className="rounded-2xl border border-emerald-900/70 bg-emerald-950/15 p-4 @[640px]:p-5">
            <p className="text-xs font-bold tracking-[0.16em] text-emerald-400">STORAGE</p>
            <h2 className="mt-1 text-lg font-black text-white">服务器磁盘空间</h2>
            <p className="mt-1 text-xs leading-5 text-gray-400">容量快照由服务端低频缓存，不跟随页面的 5 秒状态轮询重复读取磁盘。</p>
            {storage ? (
              <div className="mt-5">
                <div className="flex items-end justify-between gap-3">
                  <div>
                    <p className="text-xs text-gray-500">可用空间</p>
                    <p className="mt-1 text-2xl font-black text-white">{formatBytes(storage.availableBytes)}</p>
                  </div>
                  <span className={`rounded-full px-2.5 py-1 text-xs font-bold ${storage.healthy ? "bg-emerald-500/15 text-emerald-300" : "bg-amber-500/15 text-amber-300"}`}>
                    {storage.healthy ? "空间正常" : "需要关注"}
                  </span>
                </div>
                <div className="mt-4 h-2 overflow-hidden rounded-full bg-gray-800" aria-label={`磁盘已使用 ${usedStoragePercent?.toFixed(1) ?? 0}%`}>
                  <div className={`h-full rounded-full ${storage.healthy ? "bg-emerald-400" : "bg-amber-400"}`} style={{ width: `${usedStoragePercent ?? 0}%` }} />
                </div>
                <dl className="mt-4 grid grid-cols-2 gap-3 text-xs">
                  <div className="rounded-xl bg-gray-950/70 p-3"><dt className="text-gray-500">总容量</dt><dd className="mt-1 font-bold text-gray-200">{formatBytes(storage.totalBytes)}</dd></div>
                  <div className="rounded-xl bg-gray-950/70 p-3"><dt className="text-gray-500">已使用</dt><dd className="mt-1 font-bold text-gray-200">{usedStoragePercent?.toFixed(1) ?? "—"}%</dd></div>
                </dl>
                <p className="mt-3 text-[11px] leading-5 text-gray-500">采样于 {formatTimestamp(storage.updatedAt)} · 每 {storage.refreshIntervalHours} 小时更新</p>
              </div>
            ) : (
              <div className="mt-5 rounded-xl border border-dashed border-gray-800 px-4 py-8 text-center text-sm text-gray-500">等待首次磁盘容量采样</div>
            )}
          </section>

          <section aria-label="玩家账号管理" className="rounded-2xl border border-fuchsia-900/70 bg-fuchsia-950/10 p-4 @[640px]:p-5">
            <p className="text-xs font-bold tracking-[0.16em] text-fuchsia-400">PLAYER ADMIN</p>
            <h2 className="mt-1 text-lg font-black text-white">玩家账号管理</h2>
            <p className="mt-1 text-xs leading-5 text-gray-400">先按账号或昵称搜索并选中玩家。账号本身不可修改；改名和密码重置均记录管理员、目标账号与操作时间。</p>

            <form className="mt-4 flex flex-col gap-2 @[520px]:flex-row" onSubmit={(event) => { event.preventDefault(); searchPlayers(); }}>
              <input
                value={playerQuery}
                onChange={(event) => setPlayerQuery(event.target.value)}
                placeholder="输入玩家账号或昵称"
                aria-label="搜索要管理的玩家"
                maxLength={32}
                className="min-h-11 min-w-0 flex-1 rounded-xl border border-gray-800 bg-gray-950 px-3 text-sm text-white outline-none placeholder:text-gray-600 focus:border-fuchsia-500"
              />
              <button type="submit" disabled={!connected || !playerQuery.trim()} className="min-h-11 rounded-xl bg-fuchsia-500 px-5 text-sm font-black text-white hover:bg-fuchsia-400 disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-600">搜索玩家</button>
            </form>

            <div className="mt-4 grid gap-4 @[680px]:grid-cols-[minmax(0,0.8fr)_minmax(0,1.2fr)]">
              <div className="max-h-64 space-y-2 overflow-y-auto pr-1" aria-label="玩家搜索结果">
                {adminPlayerSearchResults.length ? adminPlayerSearchResults.map((player) => (
                  <button
                    key={player.account}
                    type="button"
                    onClick={() => selectPlayer(player)}
                    aria-pressed={selectedPlayerAccount === player.account}
                    className={`flex min-h-14 w-full items-center justify-between gap-3 rounded-xl border px-3 text-left ${selectedPlayerAccount === player.account ? "border-fuchsia-400 bg-fuchsia-950/40" : "border-gray-800 bg-gray-950 hover:border-gray-700"}`}
                  >
                    <span className="min-w-0"><span className="block truncate text-sm font-bold text-white">{player.displayName}</span><span className="mt-0.5 block truncate text-xs text-gray-500">账号 {player.account}</span></span>
                    <span className={`h-2.5 w-2.5 shrink-0 rounded-full ${player.online ? "bg-emerald-400" : "bg-gray-700"}`} aria-label={player.online ? "在线" : "离线"} />
                  </button>
                )) : (
                  <div className="flex min-h-28 items-center justify-center rounded-xl border border-dashed border-gray-800 px-4 text-center text-xs leading-5 text-gray-600">搜索结果会显示在这里；选择玩家后才会开放账号操作。</div>
                )}
              </div>

              <div className="rounded-xl border border-gray-800 bg-gray-950/70 p-4">
                {selectedPlayer ? (
                  <>
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <div><p className="text-sm font-black text-white">{selectedPlayer.displayName}</p><p className="mt-1 text-xs text-gray-500">账号 {selectedPlayer.account}</p></div>
                      <span className={`rounded-full px-2.5 py-1 text-xs font-bold ${selectedPlayer.online ? "bg-emerald-500/15 text-emerald-300" : "bg-gray-800 text-gray-400"}`}>{selectedPlayer.online ? "当前在线" : "当前离线"}</span>
                    </div>
                    <p className="mt-2 text-[11px] text-gray-600">最近登录 {formatTimestamp(selectedPlayer.lastLoginAt)} · {selectedPlayer.hasPassword ? "已设置密码" : "尚未设置密码"}</p>
                    <div className="mt-4">
                      <label htmlFor="admin-player-name" className="text-xs font-bold text-gray-400">修改昵称</label>
                      <div className="mt-2 flex flex-col gap-2 @[520px]:flex-row">
                        <input id="admin-player-name" value={renameValue} onChange={(event) => setRenameValue(event.target.value)} maxLength={32} className="min-h-11 min-w-0 flex-1 rounded-xl border border-gray-800 bg-gray-900 px-3 text-sm text-white outline-none focus:border-fuchsia-500" />
                        <button type="button" onClick={renamePlayer} disabled={!renameValue.trim() || renameValue.trim() === selectedPlayer.displayName} className="min-h-11 rounded-xl border border-fuchsia-700 px-4 text-sm font-bold text-fuchsia-200 hover:bg-fuchsia-950/50 disabled:cursor-not-allowed disabled:border-gray-800 disabled:text-gray-600">确认改名</button>
                      </div>
                      <p className="mt-2 text-[11px] leading-5 text-gray-600">管理员改名不会消耗或恢复玩家自己的一次改名机会。</p>
                    </div>
                    <div className="mt-4 border-t border-gray-800 pt-4">
                      <button
                        type="button"
                        onClick={resetPlayerPassword}
                        disabled={selectedPlayer.account.toLocaleLowerCase() === account.toLocaleLowerCase()}
                        className="min-h-11 w-full rounded-xl bg-red-500 px-4 text-sm font-black text-white hover:bg-red-400 disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-500"
                      >
                        {selectedPlayer.account.toLocaleLowerCase() === account.toLocaleLowerCase() ? "不能重置当前账号" : "重置密码并注销旧会话"}
                      </button>
                      <p className="mt-2 text-[11px] leading-5 text-gray-600">系统生成强随机临时密码，只在本次操作结果中展示；玩家登录后可在个人中心自行修改。</p>
                    </div>
                    {adminTemporaryPassword?.account === selectedPlayer.account && (
                      <div className="mt-4 rounded-xl border border-amber-700/70 bg-amber-950/30 p-3" aria-live="polite">
                        <p className="text-xs font-bold text-amber-300">临时密码（请立即交给玩家）</p>
                        <div className="mt-2 flex flex-col gap-2 @[460px]:flex-row">
                          <code className="min-h-11 min-w-0 flex-1 select-all overflow-x-auto rounded-lg bg-gray-950 px-3 py-3 text-sm text-white">{adminTemporaryPassword.password}</code>
                          <button type="button" onClick={copyTemporaryPassword} className="min-h-11 rounded-lg bg-amber-500 px-4 text-sm font-black text-gray-950">复制密码</button>
                        </div>
                      </div>
                    )}
                  </>
                ) : (
                  <div className="flex min-h-52 items-center justify-center px-4 text-center text-sm leading-6 text-gray-600">请从左侧搜索结果中选择一个玩家。所有写操作都会再次由服务端校验管理员权限。</div>
                )}
              </div>
            </div>
          </section>
        </div>

        <div className="mt-6">
          <RulesetControlPanel />
        </div>

        <section aria-label="版本发布" className="mt-6 rounded-2xl border border-sky-900/70 bg-sky-950/15 p-4 @[640px]:p-5">
          <div>
            <p className="text-xs font-bold tracking-[0.16em] text-sky-400">RELEASE CONTROL</p>
            <h2 className="mt-1 text-lg font-black text-white">版本发布</h2>
            <p className="mt-1 text-xs leading-5 text-gray-400">当前运行版本 {shortCommit(adminOperations.currentCommit)}。任务始终读取远端 main 最新提交，并由服务器安全执行器串行处理。</p>
          </div>
          <div className="mt-4 grid gap-4 @[760px]:grid-cols-2">
            {([adminOperations.test, adminOperations.production] as const).map((status) => {
              const production = status.environment === "production";
              const busy = status.state === "queued" || status.state === "running";
              return (
                <article key={status.environment} className={`rounded-xl border p-4 ${production ? "border-red-900/70 bg-red-950/20" : "border-cyan-900/70 bg-cyan-950/20"}`}>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div>
                      <p className={`text-sm font-black ${production ? "text-red-200" : "text-cyan-200"}`}>{production ? "正式服" : "测试服"}</p>
                      <p className="mt-1 text-xs text-gray-500">已部署 {shortCommit(status.deployedCommit)} · {deploymentLabels[status.state]}</p>
                    </div>
                    <span className={`rounded-full px-2.5 py-1 text-xs font-bold ${status.state === "failed" ? "bg-red-500/20 text-red-300" : busy ? "bg-amber-500/20 text-amber-300" : "bg-gray-900 text-gray-400"}`}>
                      {deploymentLabels[status.state]}
                    </span>
                  </div>
                  <p className="mt-3 min-h-10 text-xs leading-5 text-gray-400" aria-live="polite">{status.message}</p>
                  <button
                    type="button"
                    onClick={() => deployLatest(status.environment)}
                    disabled={!connected || !adminOperations.deploymentAvailable || busy}
                    className={`mt-4 min-h-11 w-full rounded-xl px-4 text-sm font-black disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-600 ${production ? "bg-red-500 text-white hover:bg-red-400" : "bg-cyan-500 text-gray-950 hover:bg-cyan-400"}`}
                  >
                    {busy ? "发布任务处理中" : production ? "一键发布正式服到最新" : "一键部署测试服到最新"}
                  </button>
                </article>
              );
            })}
          </div>
          <p className="mt-3 text-xs leading-5 text-amber-200/70">正式发布会拒绝未在测试服验证、更新日志未归档或仍有进行中房间的版本，不会绕过现有发布门禁。</p>
        </section>

        <aside className="mt-4 rounded-xl border border-gray-800 bg-gray-950/70 px-4 py-3 text-xs leading-5 text-gray-500">
          权限与操作结果均由服务器再次校验。维护、版本切换与内容审核等操作不会仅依赖网页端判断。
        </aside>
      </div>
    </section>
  );
}
