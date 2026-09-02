"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { showMessage } from "@/components/ui/MessageBox";
import { positionLineChartTooltip } from "@/lib/lineChartTooltip";
import { normalizeQq } from "@/lib/qqWhitelist.mjs";
import { HomeRequest } from "@/net/HomeProtocol";
import { useNetStore } from "@/store/netStore";
import type {
  AdminDeploymentEnvironment,
  AdminDeploymentStatus,
  DailyActivePlayerPoint,
  DailyMatchCountPoint,
  OnlinePlayerPeakPoint,
} from "@/types/net";
import RulesetControlPanel from "./RulesetControlPanel";
import AdminHexCatalogPanel from "./AdminHexCatalogPanel";
import QqWhitelistImportPanel from "./QqWhitelistImportPanel";
import OperationsWorkbench from "./OperationsWorkbench";

type AdminPanelProps = {
  onOpenCardBackReview: () => void;
  onOpenPlayers: () => void;
  onReturnToLobby: () => void;
};

type ExpandedTrend = "peak" | "dailyActive" | "matches" | null;

function StatusCard({
  label,
  value,
  detail,
  tone,
  className = "",
  onClick,
  expanded,
  controls,
}: {
  label: string;
  value: string | number;
  detail: string;
  tone: "emerald" | "amber" | "cyan" | "violet";
  className?: string;
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

  const interactiveClasses = expanded
    ? "border-white/80 bg-gray-800/90 shadow-[0_0_0_2px_rgba(255,255,255,0.16),0_0_24px_rgba(34,211,238,0.18)]"
    : "hover:-translate-y-0.5 hover:border-cyan-400";

  const content = (
    <>
      <div className="flex min-w-0 items-center justify-between gap-2">
        <p className="min-w-0 text-xs font-bold tracking-[0.12em] text-gray-400">{label}</p>
        {onClick && (
          <span
            className={`inline-flex shrink-0 items-center gap-1 rounded-full border px-2 py-1 text-[11px] font-black leading-none ${expanded ? "border-white/60 bg-white/15 text-white" : "border-current/30 bg-black/20"}`}
            aria-hidden="true"
          >
            {expanded ? "已展开" : "点击查看"}
            <span className="text-sm leading-none">{expanded ? "▲" : "▼"}</span>
          </span>
        )}
      </div>
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
        data-selected={expanded ? "true" : "false"}
        className={`min-h-28 min-w-0 rounded-2xl border p-4 text-left transition focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-cyan-300 ${toneClasses} ${interactiveClasses} ${className}`}
      >
        {content}
      </button>
    );
  }
  return <article className={`min-w-0 rounded-2xl border p-4 ${toneClasses} ${className}`}>{content}</article>;
}

type InteractiveChartPoint = {
  key: string;
  label: string;
  value: number;
  x: number;
  y: number;
};

function InteractiveLinePoints({
  points,
  width,
  height,
  unit,
  valueLabel,
  dotFill,
  dotStroke,
  accent,
}: {
  points: InteractiveChartPoint[];
  width: number;
  height: number;
  unit: string;
  valueLabel: string;
  dotFill: string;
  dotStroke: string;
  accent: string;
}) {
  const [hoveredKey, setHoveredKey] = useState<string | null>(null);
  const [focusedKey, setFocusedKey] = useState<string | null>(null);
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const activeKey = hoveredKey ?? focusedKey ?? selectedKey;
  const activePoint = points.find((point) => point.key === activeKey) ?? null;
  const tooltipWidth = 168;
  const tooltipHeight = 58;
  const tooltipPosition = activePoint
    ? positionLineChartTooltip(activePoint.x, activePoint.y, width, height, tooltipWidth, tooltipHeight)
    : { x: 0, y: 0 };

  return (
    <>
      {points.map((point) => {
        const active = point.key === activeKey;
        const toggleSelected = () => setSelectedKey((current) => current === point.key ? null : point.key);
        return (
          <g
            key={point.key}
            role="button"
            tabIndex={0}
            focusable="true"
            aria-label={`日期：${point.label}，${valueLabel}：${point.value} ${unit}`}
            data-line-point={point.key}
            className="cursor-pointer outline-none"
            onPointerEnter={() => setHoveredKey(point.key)}
            onPointerLeave={() => setHoveredKey(null)}
            onFocus={() => setFocusedKey(point.key)}
            onBlur={() => setFocusedKey(null)}
            onClick={toggleSelected}
            onKeyDown={(event) => {
              if (event.key === "Enter" || event.key === " ") {
                event.preventDefault();
                toggleSelected();
              } else if (event.key === "Escape") {
                setSelectedKey(null);
                event.currentTarget.blur();
              }
            }}
          >
            <circle cx={point.x} cy={point.y} r="16" fill="transparent" stroke="transparent" pointerEvents="all" />
            {active && <circle cx={point.x} cy={point.y} r="10" fill={accent} fillOpacity="0.18" stroke={accent} strokeWidth="1.5" pointerEvents="none" />}
            <circle cx={point.x} cy={point.y} r={active ? 6 : 5} fill={dotFill} stroke={dotStroke} strokeWidth="2" pointerEvents="none" />
          </g>
        );
      })}
      {activePoint && (
        <g data-line-tooltip={activePoint.key} pointerEvents="none" aria-hidden="true">
          <line x1={activePoint.x} x2={activePoint.x} y1="24" y2={height - 24} stroke={accent} strokeWidth="1" strokeDasharray="4 4" opacity="0.45" />
          <rect x={tooltipPosition.x} y={tooltipPosition.y} width={tooltipWidth} height={tooltipHeight} rx="10" fill="rgb(3 7 18)" fillOpacity="0.97" stroke={accent} strokeWidth="1.5" />
          <text x={tooltipPosition.x + 12} y={tooltipPosition.y + 21} fill="rgb(156 163 175)" fontSize="12">日期：{activePoint.label}</text>
          <text x={tooltipPosition.x + 12} y={tooltipPosition.y + 44} fill="white" fontSize="17" fontWeight="800">{valueLabel}：{activePoint.value} {unit}</text>
        </g>
      )}
    </>
  );
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
        <InteractiveLinePoints
          points={chart.coordinates.map((point) => ({ key: point.date, label: point.date, value: point.count, x: point.x, y: point.y }))}
          width={chart.width}
          height={chart.height}
          unit="场"
          valueLabel="场次"
          dotFill="rgb(109 40 217)"
          dotStroke="rgb(221 214 254)"
          accent="rgb(167 139 250)"
        />
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

function PlayerCountChart({
  points,
  chartName,
  emptyMessage,
}: {
  points: OnlinePlayerPeakPoint[];
  chartName: "峰值在线玩家" | "日活玩家";
  emptyMessage: string;
}) {
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
    return <div className="flex min-h-48 items-center justify-center px-4 text-center text-sm text-gray-500">{emptyMessage}</div>;
  }

  return (
    <div>
      <svg
        viewBox={`0 0 ${chart.width} ${chart.height}`}
        className="h-auto min-h-48 w-full overflow-visible"
        role="img"
        aria-label={`${chartName}折线图，最高 ${chart.displayMax} 人`}
      >
        {[0, 0.5, 1].map((ratio) => {
          const y = 24 + ratio * 172;
          return <line key={ratio} x1="32" x2="688" y1={y} y2={y} stroke="rgb(31 41 55)" strokeWidth="1" />;
        })}
        <polygon points={chart.area} fill="rgba(34,211,238,0.12)" />
        <polyline points={chart.line} fill="none" stroke="rgb(34 211 238)" strokeWidth="4" strokeLinejoin="round" strokeLinecap="round" />
        <InteractiveLinePoints
          points={chart.coordinates.map((point) => ({ key: point.date, label: point.date, value: point.peak, x: point.x, y: point.y }))}
          width={chart.width}
          height={chart.height}
          unit="人"
          valueLabel="人数"
          dotFill="rgb(8 145 178)"
          dotStroke="rgb(165 243 252)"
          accent="rgb(34 211 238)"
        />
      </svg>
      <div className="mt-2 flex justify-between text-[11px] text-gray-500">
        <span>{points[0]?.date.slice(5)}</span>
        <span>最高 {chart.displayMax} 人</span>
        <span>{points.at(-1)?.date.slice(5)}</span>
      </div>
      <table className="sr-only">
        <caption>{chartName}</caption>
        <thead><tr><th>日期</th><th>人数</th></tr></thead>
        <tbody>{points.map((point) => <tr key={point.date}><td>{point.date}</td><td>{point.peak}</td></tr>)}</tbody>
      </table>
    </div>
  );
}

function DailyActiveChart({ points }: { points: DailyActivePlayerPoint[] }) {
  return (
    <PlayerCountChart
      points={points.map((point) => ({ date: point.date, peak: point.count }))}
      chartName="日活玩家"
      emptyMessage="正式服日活统计尚未启用"
    />
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
  const adminApproval = useNetStore((state) => state.operationsWorkbench.approval);
  const adminPlayerSearchResults = useNetStore((state) => state.adminPlayerSearchResults);
  const adminTemporaryPassword = useNetStore((state) => state.adminTemporaryPassword);
  const [announcement, setAnnouncement] = useState("");
  const [lastRefreshAt, setLastRefreshAt] = useState<Date | null>(null);
  const [expandedTrend, setExpandedTrend] = useState<ExpandedTrend>(null);
  const [peakRange, setPeakRange] = useState<7 | 30>(7);
  const [dailyActiveRange, setDailyActiveRange] = useState<7 | 30>(7);
  const [matchRange, setMatchRange] = useState<7 | 30>(7);
  const [playerQuery, setPlayerQuery] = useState("");
  const [playerSearchBy, setPlayerSearchBy] = useState<"player" | "qq">("player");
  const [selectedPlayerAccount, setSelectedPlayerAccount] = useState<string | null>(null);
  const [renameValue, setRenameValue] = useState("");
  const [qqBindingValue, setQqBindingValue] = useState("");
  const qqMutationRequestRef = useRef<{ fingerprint: string; requestId: string } | null>(null);

  const connected = connState === "connected";
  const pendingReviews = reviewQueue?.length;
  const authoritativeOnlineCount = adminOperations.onlineCount ?? "—";
  const todayPeak = adminOperations.peaks7.at(-1)?.peak ?? "—";
  const todayDailyActive = adminOperations.dailyActive7.at(-1)?.count ?? "—";
  const todayMatches = adminOperations.matches7.at(-1)?.count ?? "—";
  const storage = adminOperations.storage;
  const usedStoragePercent = storage?.totalBytes
    ? Math.max(0, Math.min(100, ((storage.totalBytes - storage.availableBytes) / storage.totalBytes) * 100))
    : null;
  const selectedPlayer = adminPlayerSearchResults.find((player) => player.account === selectedPlayerAccount) ?? null;

  const toggleTrend = (trend: Exclude<ExpandedTrend, null>) => {
    setExpandedTrend((current) => current === trend ? null : trend);
  };

  useEffect(() => {
    if (!connected || !maintenance.canManage) return;
    HomeRequest.requestMaintenanceState();
    HomeRequest.requestRulesetState();
    HomeRequest.requestCardBackReviewQueue();
    HomeRequest.requestAdminOperations();
    HomeRequest.requestQqWhitelistStatus();
    setLastRefreshAt(new Date());
  }, [connected, maintenance.canManage]);

  useEffect(() => {
    if (!connected || !maintenance.canManage) return;
    const timer = window.setInterval(() => HomeRequest.requestAdminOperations(), 5_000);
    return () => window.clearInterval(timer);
  }, [connected, maintenance.canManage]);

  useEffect(() => {
    if (!selectedPlayer) return;
    setQqBindingValue(selectedPlayer.qq ?? "");
    qqMutationRequestRef.current = null;
  }, [selectedPlayer?.account, selectedPlayer?.bindingRevision]);

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
    HomeRequest.requestQqWhitelistStatus();
    setLastRefreshAt(new Date());
  };

  const deployLatest = (environment: AdminDeploymentEnvironment) => {
    const production = environment === "production";
    const operation = production ? "deploy_production" : "deploy_test";
    const approvalReady = adminApproval?.operation === operation
      && adminApproval.target === environment
      && adminApproval.expiresAt > Date.now();
    if (!approvalReady) {
      const confirmed = window.confirm(production
        ? "这是正式服高风险操作。确认申请一次性发布凭证？申请后必须再次核对目标并点击执行，现阶段不会发布。"
        : "确认申请测试服部署的一次性凭证？申请后必须再次点击执行，现阶段不会重启服务。");
      if (!confirmed) return;
      if (!HomeRequest.requestAdminApproval(operation, environment)) showMessage("服务器未连接，无法申请发布凭证", "error");
      return;
    }
    const confirmed = window.confirm(production
      ? "二次确认：立即将远端 main 最新版本发布到正式服？系统仍会检查测试服验证、日志归档与进行中房间。该凭证执行后失效。"
      : "二次确认：立即将远端 main 最新版本部署到测试服？测试服会短暂重启，该凭证执行后失效。");
    if (!confirmed) return;
    if (!HomeRequest.deployLatest(environment, adminApproval)) {
      showMessage("服务器未连接，无法提交发布任务", "error");
      return;
    }
    useNetStore.getState().setAdminApproval(null);
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
    let query = playerQuery.trim();
    if (!query) {
      showMessage(playerSearchBy === "qq" ? "请输入 QQ 号" : "请输入玩家账号或昵称", "error");
      return;
    }
    if (playerSearchBy === "qq") {
      try { query = normalizeQq(query); }
      catch (validationError) {
        showMessage(validationError instanceof Error ? validationError.message : "QQ 格式无效", "error");
        return;
      }
    }
    setSelectedPlayerAccount(null);
    setRenameValue("");
    setQqBindingValue("");
    qqMutationRequestRef.current = null;
    useNetStore.getState().setAdminPlayerSearchResults([]);
    HomeRequest.searchAdminPlayers(query, playerSearchBy);
  };

  const selectPlayer = (target: (typeof adminPlayerSearchResults)[number]) => {
    setSelectedPlayerAccount(target.account);
    setRenameValue(target.displayName);
    setQqBindingValue(target.qq ?? "");
    qqMutationRequestRef.current = null;
    useNetStore.getState().setAdminTemporaryPassword(null);
  };

  const qqMutationRequestId = (fingerprint: string) => {
    if (qqMutationRequestRef.current?.fingerprint === fingerprint) {
      return qqMutationRequestRef.current.requestId;
    }
    const requestId = crypto.randomUUID();
    qqMutationRequestRef.current = { fingerprint, requestId };
    return requestId;
  };

  const setPlayerQq = () => {
    if (!selectedPlayer) return;
    let normalizedQq: string;
    try { normalizedQq = normalizeQq(qqBindingValue); }
    catch (validationError) {
      showMessage(validationError instanceof Error ? validationError.message : "QQ 格式无效", "error");
      return;
    }
    if (normalizedQq === selectedPlayer.qq) {
      showMessage("新 QQ 与当前绑定相同", "error");
      return;
    }
    const revision = selectedPlayer.bindingRevision ?? 0;
    if (!window.confirm(
      `确认将“${selectedPlayer.displayName}”（账号 ${selectedPlayer.account}）绑定到 QQ ${normalizedQq}？旧登录令牌会失效；若玩家正在对局，本局仍可继续。`,
    )) return;
    const requestId = qqMutationRequestId(`set:${selectedPlayer.account}:${revision}:${normalizedQq}`);
    if (!HomeRequest.setAdminPlayerQq(selectedPlayer.account, normalizedQq, revision, requestId)) {
      showMessage("服务器未连接，QQ 绑定没有提交", "error");
    }
  };

  const unbindPlayerQq = () => {
    if (!selectedPlayer?.qqBound) return;
    const revision = selectedPlayer.bindingRevision ?? 0;
    if (!window.confirm(
      `确认解绑“${selectedPlayer.displayName}”（账号 ${selectedPlayer.account}）当前 QQ ${selectedPlayer.qq ?? selectedPlayer.qqMasked ?? ""}？玩家需要重新绑定后才能进入大厅或新对局。`,
    )) return;
    const requestId = qqMutationRequestId(`unbind:${selectedPlayer.account}:${revision}`);
    if (!HomeRequest.unbindAdminPlayerQq(selectedPlayer.account, revision, requestId)) {
      showMessage("服务器未连接，解绑没有提交", "error");
    }
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
    const approvalReady = adminApproval?.operation === "reset_password"
      && adminApproval.target === selectedPlayer.account
      && adminApproval.expiresAt > Date.now();
    if (!approvalReady) {
      if (!window.confirm(`确认申请账号“${selectedPlayer.account}”密码重置的一次性凭证？此步骤不会修改密码，申请后仍需再次确认。`)) return;
      if (!HomeRequest.requestAdminApproval("reset_password", selectedPlayer.account)) {
        showMessage("服务器未连接，无法申请密码重置凭证", "error");
      }
      return;
    }
    if (!window.confirm(`二次确认：立即重置账号“${selectedPlayer.account}”的密码并注销其全部登录会话？该凭证执行后失效。`)) return;
    if (!HomeRequest.resetAdminPlayerPassword(selectedPlayer.account, adminApproval)) {
      showMessage("服务器未连接，密码重置没有提交", "error");
      return;
    }
    useNetStore.getState().setAdminApproval(null);
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

        <div className="mt-6 grid grid-cols-2 gap-3 @[720px]:grid-cols-3 @[1040px]:grid-cols-4">
          <StatusCard
            label="服务连接"
            value={connected ? "已连接" : "未连接"}
            detail={!connected ? "等待服务器恢复连接" : maintenance.enabled ? "维护模式运行中" : "游戏服务正常开放"}
            tone={connected ? "emerald" : "amber"}
          />
          <StatusCard
            label="峰值在线玩家"
            value={todayPeak}
            detail={expandedTrend === "peak" ? "趋势已展开 · 点击收起" : "点击查看近一周/月峰值在线玩家"}
            tone="cyan"
            onClick={() => toggleTrend("peak")}
            expanded={expandedTrend === "peak"}
            controls="online-peak-panel"
          />
          <StatusCard
            label="日活玩家"
            value={todayDailyActive}
            detail={expandedTrend === "dailyActive" ? "趋势已展开 · 点击收起" : "点击查看近一周/月日活玩家"}
            tone="cyan"
            onClick={() => toggleTrend("dailyActive")}
            expanded={expandedTrend === "dailyActive"}
            controls="daily-active-panel"
          />
          <StatusCard
            label="今日完成场次"
            value={todayMatches}
            detail={expandedTrend === "matches" ? "趋势已展开 · 点击收起" : "点击查看近一周/月场次"}
            tone="violet"
            onClick={() => toggleTrend("matches")}
            expanded={expandedTrend === "matches"}
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
            className="col-span-2 @[720px]:col-span-3 @[1040px]:col-span-2"
          />
        </div>

        {expandedTrend === "peak" && (
          <section id="online-peak-panel" aria-label="峰值在线玩家" className="mt-4 rounded-2xl border border-cyan-900/70 bg-cyan-950/15 p-4 @[640px]:p-5">
            <div className="flex flex-col gap-3 @[520px]:flex-row @[520px]:items-center @[520px]:justify-between">
              <div>
                <p className="text-xs font-bold tracking-[0.16em] text-cyan-400">PLAYER TRAFFIC</p>
                <h2 className="mt-1 text-lg font-black text-white">峰值在线玩家</h2>
                <p className="mt-1 text-xs leading-5 text-gray-400">按 UTC+8 自然日统计正式服单日最高在线人数；测试服与正式服均展示正式服权威数据，服务端每分钟更新展示缓存。</p>
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
              <div className="min-w-[34rem]">
                <PlayerCountChart
                  points={peakRange === 7 ? adminOperations.peaks7 : adminOperations.peaks30}
                  chartName="峰值在线玩家"
                  emptyMessage="正式服峰值统计尚未启用"
                />
              </div>
            </div>
            <p className="mt-2 text-[11px] text-gray-500">统计缓存更新于 {formatTimestamp(adminOperations.playerTrafficUpdatedAt)}</p>
          </section>
        )}

        {expandedTrend === "dailyActive" && (
          <section id="daily-active-panel" aria-label="日活玩家" className="mt-4 rounded-2xl border border-sky-900/70 bg-sky-950/15 p-4 @[640px]:p-5">
            <div className="flex flex-col gap-3 @[520px]:flex-row @[520px]:items-center @[520px]:justify-between">
              <div>
                <p className="text-xs font-bold tracking-[0.16em] text-sky-400">DAILY ACTIVE PLAYERS</p>
                <h2 className="mt-1 text-lg font-black text-white">日活玩家</h2>
                <p className="mt-1 text-xs leading-5 text-gray-400">按 UTC+8 自然日统计正式服当天至少成功登录一次的去重玩家；测试服登录不会计入，服务端每分钟更新展示缓存。</p>
              </div>
              <div className="grid grid-cols-2 rounded-xl border border-gray-800 bg-gray-950 p-1" aria-label="日活玩家统计周期">
                {([7, 30] as const).map((range) => (
                  <button
                    key={range}
                    type="button"
                    onClick={() => setDailyActiveRange(range)}
                    aria-pressed={dailyActiveRange === range}
                    className={`min-h-11 rounded-lg px-4 text-sm font-bold ${dailyActiveRange === range ? "bg-sky-500 text-gray-950" : "text-gray-400 hover:text-white"}`}
                  >
                    近{range === 7 ? "一周" : "一月"}
                  </button>
                ))}
              </div>
            </div>
            <div className="mt-4 overflow-x-auto">
              <div className="min-w-[34rem]"><DailyActiveChart points={dailyActiveRange === 7 ? adminOperations.dailyActive7 : adminOperations.dailyActive30} /></div>
            </div>
            <p className="mt-2 text-[11px] text-gray-500">统计缓存更新于 {formatTimestamp(adminOperations.playerTrafficUpdatedAt)}</p>
          </section>
        )}

        {expandedTrend === "matches" && (
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

        <div className="mt-6">
          <QqWhitelistImportPanel />
        </div>

        <div className="mt-6">
          <OperationsWorkbench />
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
            <p className="mt-1 text-xs leading-5 text-gray-400">可按账号/昵称模糊查询绑定 QQ，也可用完整 QQ 反查玩家。重名和模糊命中会全部列出，请同时核对账号；改绑、解绑、改名和密码重置均由服务端鉴权并审计。</p>

            <div className="mt-4 grid grid-cols-2 gap-2 rounded-xl border border-gray-800 bg-gray-950 p-1" aria-label="玩家搜索方式">
              {(["player", "qq"] as const).map((mode) => (
                    <button
                  key={mode}
                  type="button"
                  onClick={() => {
                    setPlayerSearchBy(mode);
                    setPlayerQuery("");
                    setSelectedPlayerAccount(null);
                    useNetStore.getState().setAdminPlayerSearchResults([]);
                  }}
                  aria-pressed={playerSearchBy === mode}
                  className={`min-h-11 rounded-lg px-3 text-sm font-bold ${playerSearchBy === mode ? "bg-fuchsia-500 text-white" : "text-gray-400 hover:bg-gray-900 hover:text-white"}`}
                >
                  {mode === "player" ? "按账号 / 昵称" : "按 QQ 反查"}
                </button>
              ))}
            </div>

            <form className="mt-4 flex flex-col gap-2 @[520px]:flex-row" onSubmit={(event) => { event.preventDefault(); searchPlayers(); }}>
              <input
                value={playerQuery}
                onChange={(event) => setPlayerQuery(event.target.value)}
                placeholder={playerSearchBy === "qq" ? "输入完整 QQ 号" : "输入玩家账号或昵称"}
                aria-label={playerSearchBy === "qq" ? "通过 QQ 号反查绑定玩家" : "通过账号或昵称搜索玩家"}
                inputMode={playerSearchBy === "qq" ? "numeric" : "text"}
                autoComplete="off"
                maxLength={playerSearchBy === "qq" ? 12 : 32}
                className="min-h-11 min-w-0 flex-1 rounded-xl border border-gray-800 bg-gray-950 px-3 text-sm text-white outline-none placeholder:text-gray-600 focus:border-fuchsia-500"
              />
              <button type="submit" disabled={!connected || !playerQuery.trim()} className="min-h-11 rounded-xl bg-fuchsia-500 px-5 text-sm font-black text-white hover:bg-fuchsia-400 disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-600">{playerSearchBy === "qq" ? "反查玩家" : "搜索玩家"}</button>
            </form>

            {adminPlayerSearchResults.length > 0 && (
              <p className="mt-2 text-xs leading-5 text-gray-500" role="status">
                共 {adminPlayerSearchResults.length} 条结果{adminPlayerSearchResults.length > 1 ? "；存在重名或模糊命中，请按账号逐一核对。" : "。"}
              </p>
            )}

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
                    <span className="min-w-0">
                      <span className="flex min-w-0 items-center gap-2">
                        <span className="truncate text-sm font-bold text-white">{player.displayName}</span>
                        {player.matchKind === "fuzzy" && <span className="shrink-0 rounded bg-gray-800 px-1.5 py-0.5 text-[10px] font-bold text-gray-400">模糊命中</span>}
                        {player.matchKind === "nickname_exact" && <span className="shrink-0 rounded bg-fuchsia-500/15 px-1.5 py-0.5 text-[10px] font-bold text-fuchsia-300">昵称完全匹配</span>}
                      </span>
                      <span className="mt-0.5 block truncate text-xs text-gray-500">账号 {player.account} · QQ {player.qq ?? "未绑定"}</span>
                    </span>
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
                    <p className={`mt-1 text-xs font-bold ${selectedPlayer.qqCurrentlyWhitelisted ? "text-emerald-300" : selectedPlayer.qqBound ? "text-red-300" : "text-amber-300"}`}>
                      QQ：{selectedPlayer.qqBound ? `${selectedPlayer.qq ?? selectedPlayer.qqMasked ?? "已绑定"}${selectedPlayer.qqCurrentlyWhitelisted ? " · 当前在白名单" : " · 当前已移出白名单"}` : "尚未绑定"} · 绑定版本 {selectedPlayer.bindingRevision ?? 0}
                    </p>
                    <div className="mt-4 rounded-xl border border-cyan-900/70 bg-cyan-950/15 p-3">
                      <label htmlFor="admin-player-qq" className="text-xs font-bold text-cyan-200">修改绑定 QQ</label>
                      <div className="mt-2 flex flex-col gap-2 @[520px]:flex-row">
                        <input
                          id="admin-player-qq"
                          value={qqBindingValue}
                          onChange={(event) => setQqBindingValue(event.target.value)}
                          inputMode="numeric"
                          autoComplete="off"
                          maxLength={12}
                          placeholder="5–12 位 QQ 号"
                          className="min-h-11 min-w-0 flex-1 rounded-xl border border-gray-800 bg-gray-900 px-3 text-sm text-white outline-none focus:border-cyan-500"
                        />
                        <button type="button" onClick={setPlayerQq} disabled={!qqBindingValue.trim()} className="min-h-11 rounded-xl bg-cyan-500 px-4 text-sm font-black text-gray-950 hover:bg-cyan-400 disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-600">{selectedPlayer.qqBound ? "确认改绑" : "确认绑定"}</button>
                      </div>
                      <button type="button" onClick={unbindPlayerQq} disabled={!selectedPlayer.qqBound} className="mt-2 min-h-11 w-full rounded-xl border border-red-800 px-4 text-sm font-bold text-red-300 hover:bg-red-950/40 disabled:cursor-not-allowed disabled:border-gray-800 disabled:text-gray-600">解绑当前 QQ</button>
                      <p className="mt-2 text-[11px] leading-5 text-gray-500">只允许绑定当前白名单内且未被其他账号使用的 QQ。更新采用绑定版本校验；页面数据过期时服务端会拒绝覆盖。改绑或解绑会同时注销测试服与正式服旧登录令牌。</p>
                    </div>
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
                        {selectedPlayer.account.toLocaleLowerCase() === account.toLocaleLowerCase()
                          ? "不能重置当前账号"
                          : adminApproval?.operation === "reset_password" && adminApproval.target === selectedPlayer.account && adminApproval.expiresAt > Date.now()
                            ? "二次确认并重置密码"
                            : "申请密码重置凭证"}
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

        <div className="mt-6">
          <AdminHexCatalogPanel />
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
                    {busy
                      ? "发布任务处理中"
                      : adminApproval?.operation === (production ? "deploy_production" : "deploy_test")
                        && adminApproval.target === status.environment
                        && adminApproval.expiresAt > Date.now()
                        ? production ? "二次确认并发布正式服" : "二次确认并部署测试服"
                        : production ? "申请正式服发布凭证" : "申请测试服部署凭证"}
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
