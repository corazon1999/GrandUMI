"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { showMessage } from "@/components/ui/MessageBox";
import { HomeRequest } from "@/net/HomeProtocol";
import { useNetStore, type AdminHexCatalogState } from "@/store/netStore";
import type {
  AdminDeploymentEnvironment,
  AdminHexCatalogEnvironmentState,
  HexTierSnapshot,
} from "@/types/net";

const tierLabels: Record<HexTierSnapshot, string> = {
  Silver: "银色",
  Gold: "金色",
  Rainbow: "棱彩",
};

const tierStyles: Record<HexTierSnapshot, string> = {
  Silver: "border-slate-500/70 bg-slate-500/10 text-slate-200",
  Gold: "border-amber-500/70 bg-amber-500/10 text-amber-200",
  Rainbow: "border-fuchsia-500/70 bg-fuchsia-500/10 text-fuchsia-200",
};

const deploymentLabels: Record<AdminHexCatalogEnvironmentState["deployment"]["state"], string> = {
  idle: "待命",
  queued: "已排队",
  running: "发布中",
  success: "已完成",
  failed: "失败",
  unavailable: "不可用",
};

type TierFilter = "all" | HexTierSnapshot;
const REGULAR_HEXES_PER_TIER = 18;

function shortDigest(digest: string): string {
  if (!digest) return "—";
  return digest.startsWith("sha256:") ? digest.slice(7, 19) : digest.slice(0, 12);
}

function formatTime(value?: number | null): string {
  if (!value) return "—";
  return new Date(value).toLocaleString("zh-CN", { hour12: false });
}

function entryTierMap(state: AdminHexCatalogEnvironmentState, source: "draft" | "active") {
  return Object.fromEntries(state.entries.map((entry) => [
    entry.id,
    source === "draft" ? entry.tier : entry.activeTier,
  ])) as Record<number, HexTierSnapshot>;
}

export default function AdminHexCatalogPanel({ previewState }: { previewState?: AdminHexCatalogState }) {
  const connState = useNetStore((state) => state.connState);
  const storeCatalog = useNetStore((state) => state.adminHexCatalog);
  const approval = useNetStore((state) => state.operationsWorkbench.approval);
  const setApproval = useNetStore((state) => state.setAdminApproval);
  const [environment, setEnvironment] = useState<AdminDeploymentEnvironment>("test");
  const [tiers, setTiers] = useState<Record<number, HexTierSnapshot>>({});
  const [query, setQuery] = useState("");
  const [tierFilter, setTierFilter] = useState<TierFilter>("all");
  const [pending, setPending] = useState<"save" | "publish" | null>(null);
  const sourceKeyRef = useRef("");
  const pendingTimerRef = useRef<number | null>(null);
  const catalog = previewState ?? storeCatalog;
  const selected = catalog[environment];
  const previewOnly = previewState !== undefined;
  const connected = previewOnly || connState === "connected";

  const sourceKey = selected
    ? `${environment}:${selected.draftRevision}:${selected.draftDigest}:${selected.activeRevision}:${selected.activeDigest}`
    : `${environment}:empty`;

  useEffect(() => {
    if (!connected || previewOnly) return;
    HomeRequest.requestAdminHexCatalog();
    const timer = window.setInterval(() => HomeRequest.requestAdminHexCatalog(), 5_000);
    return () => window.clearInterval(timer);
  }, [connected, previewOnly]);

  useEffect(() => {
    if (!selected || sourceKeyRef.current === sourceKey) return;
    sourceKeyRef.current = sourceKey;
    setTiers(entryTierMap(selected, "draft"));
    setPending(null);
    if (pendingTimerRef.current) window.clearTimeout(pendingTimerRef.current);
  }, [selected, sourceKey]);

  useEffect(() => () => {
    if (pendingTimerRef.current) window.clearTimeout(pendingTimerRef.current);
  }, []);

  const sortedEntries = useMemo(
    () => [...(selected?.entries ?? [])].sort((left, right) => left.id - right.id),
    [selected?.entries],
  );
  const dirty = selected
    ? sortedEntries.some((entry) => tiers[entry.id] !== entry.tier)
    : false;
  const regularCounts = useMemo(() => {
    const counts: Record<HexTierSnapshot, number> = { Silver: 0, Gold: 0, Rainbow: 0 };
    for (const entry of sortedEntries) {
      if (!entry.alternative) counts[tiers[entry.id] ?? entry.tier] += 1;
    }
    return counts;
  }, [sortedEntries, tiers]);
  const unbalancedPool = Object.values(regularCounts).some((count) => count !== REGULAR_HEXES_PER_TIER);
  const visibleEntries = useMemo(() => {
    const normalized = query.trim().toLocaleLowerCase("zh-CN");
    return sortedEntries.filter((entry) => {
      const tier = tiers[entry.id] ?? entry.tier;
      if (tierFilter !== "all" && tier !== tierFilter) return false;
      return !normalized
        || entry.name.toLocaleLowerCase("zh-CN").includes(normalized)
        || String(entry.id).includes(normalized);
    });
  }, [query, sortedEntries, tierFilter, tiers]);

  const baseChanged = selected
    ? (selected.baseActiveRevision !== selected.activeRevision
      || selected.baseActiveDigest !== selected.activeDigest)
      && selected.draftDigest !== selected.activeDigest
    : false;
  const draftMatchesActive = selected?.draftDigest === selected?.activeDigest;
  const deploymentBusy = selected?.deployment.state === "queued" || selected?.deployment.state === "running";
  const approvalTarget = selected
    ? `${environment}:draft-${selected.draftRevision}:${selected.draftDigest}`
    : "";
  const hasApproval = approval?.operation === "publish_hex_catalog"
    && approval.target === approvalTarget
    && approval.expiresAt > Date.now();

  const armPendingTimeout = (action: "save" | "publish") => {
    setPending(action);
    if (pendingTimerRef.current) window.clearTimeout(pendingTimerRef.current);
    pendingTimerRef.current = window.setTimeout(() => setPending(null), 3_000);
  };

  const saveDraft = () => {
    if (!selected || !dirty) return;
    if (previewOnly) return;
    const sent = HomeRequest.saveAdminHexCatalog(
      environment,
      selected.draftRevision,
      selected.activeRevision,
      sortedEntries.map((entry) => ({ id: entry.id, tier: tiers[entry.id] ?? entry.tier })),
    );
    if (!sent) {
      showMessage("服务器未连接，海克斯草稿没有保存", "error");
      return;
    }
    setApproval(null);
    armPendingTimeout("save");
  };

  const publish = () => {
    if (!selected || dirty || unbalancedPool || baseChanged || draftMatchesActive || selected.draftRevision < 1) return;
    if (previewOnly) return;
    if (!hasApproval) {
      if (!HomeRequest.requestAdminApproval("publish_hex_catalog", approvalTarget)) {
        showMessage("服务器未连接，无法申请发布凭证", "error");
      }
      return;
    }
    const environmentName = environment === "production" ? "正式服" : "测试服";
    const confirmed = window.confirm(
      `确认把草稿 v${selected.draftRevision} 发布到${environmentName}？\n\n`
      + "只会原子替换海克斯品质配置，不会发布网站或代码，也不会重启服务。"
      + (environment === "production" ? "这是正式服写操作。" : ""),
    );
    if (!confirmed) return;
    const sent = HomeRequest.publishAdminHexCatalog(
      environment,
      selected.draftRevision,
      selected.draftDigest,
      { challengeId: approval!.challengeId, confirmationToken: approval!.confirmationToken },
    );
    if (!sent) {
      showMessage("服务器未连接，海克斯配置没有进入发布队列", "error");
      return;
    }
    setApproval(null);
    armPendingTimeout("publish");
  };

  const publishDisabled = !connected
    || !catalog.deploymentAvailable
    || !selected
    || dirty
    || unbalancedPool
    || baseChanged
    || draftMatchesActive
    || selected.draftRevision < 1
    || deploymentBusy
    || pending !== null;

  return (
    <section
      aria-label="海克斯品质管理"
      data-admin-hex-catalog
      className="overflow-x-hidden rounded-2xl border border-fuchsia-900/70 bg-fuchsia-950/10 p-3 pb-[max(1rem,var(--layout-safe-bottom,env(safe-area-inset-bottom)))] @[640px]:p-5"
    >
      <div className="flex flex-col gap-4 @[700px]:flex-row @[700px]:items-start @[700px]:justify-between">
        <div className="min-w-0">
          <p className="text-xs font-bold tracking-[0.16em] text-fuchsia-400">HEX CATALOG</p>
          <h2 className="mt-1 text-lg font-black text-white">海克斯品质面板</h2>
          <p className="mt-1 max-w-3xl text-xs leading-5 text-gray-400">
            编辑完整目录并先保存草稿，再通过一次性凭证发布到指定环境。草稿允许暂时不平衡；发布前，三个常规品质池必须各保持 18 个。激活只影响发布后新建的房间；进行中房间与恢复重放继续使用各自锁定版本。
          </p>
        </div>
        <button
          type="button"
          onClick={() => { if (!previewOnly) HomeRequest.requestAdminHexCatalog(); }}
          disabled={!connected || pending !== null}
          className="min-h-11 shrink-0 rounded-xl border border-fuchsia-700 bg-fuchsia-950/40 px-4 text-sm font-bold text-fuchsia-200 hover:bg-fuchsia-900/50 disabled:cursor-not-allowed disabled:opacity-50"
        >
          刷新配置
        </button>
      </div>

      <div className="mt-4 grid grid-cols-2 gap-2" role="group" aria-label="目标环境">
        {(["test", "production"] as const).map((value) => (
          <button
            key={value}
            type="button"
            aria-pressed={environment === value}
            onClick={() => {
              setEnvironment(value);
              setQuery("");
              setTierFilter("all");
              setPending(null);
            }}
            className={`min-h-11 rounded-xl border px-3 text-sm font-black ${environment === value
              ? value === "production" ? "border-red-400 bg-red-500/20 text-red-100" : "border-cyan-400 bg-cyan-500/20 text-cyan-100"
              : "border-gray-800 bg-gray-950/70 text-gray-500 hover:border-gray-600"}`}
          >
            {value === "production" ? "正式服" : "测试服"}
          </button>
        ))}
      </div>

      {!selected ? (
        <p className="mt-4 rounded-xl border border-dashed border-gray-700 px-4 py-8 text-center text-sm text-gray-500">
          {connected ? "正在读取服务端权威配置…" : "连接服务器后可管理海克斯品质。"}
        </p>
      ) : (
        <>
          <div className="mt-4 grid gap-3 @[660px]:grid-cols-2">
            <article className="rounded-xl border border-emerald-900/70 bg-emerald-950/15 p-3">
              <p className="text-xs font-black text-emerald-300">当前已发布</p>
              <p className="mt-1 text-sm font-bold text-white">v{selected.activeRevision} · {shortDigest(selected.activeDigest)}</p>
              <p className="mt-1 text-[11px] leading-5 text-gray-500">{selected.activePublishedBy ?? "内置配置"} · {formatTime(selected.activePublishedAt)}</p>
            </article>
            <article className={`rounded-xl border p-3 ${baseChanged ? "border-red-700/80 bg-red-950/20" : "border-amber-900/70 bg-amber-950/15"}`}>
              <p className={`text-xs font-black ${baseChanged ? "text-red-300" : "text-amber-300"}`}>共享草稿</p>
              <p className="mt-1 text-sm font-bold text-white">{selected.draftRevision > 0 ? `v${selected.draftRevision}` : "尚未保存"} · {shortDigest(selected.draftDigest)}</p>
              <p className="mt-1 text-[11px] leading-5 text-gray-500">{selected.draftSavedBy ?? "跟随已发布配置"} · {formatTime(selected.draftSavedAt)}</p>
            </article>
          </div>

          {baseChanged && (
            <div className="mt-3 rounded-xl border border-red-700/70 bg-red-950/25 p-3 text-xs leading-5 text-red-200" role="alert">
              目标环境已发布版本在草稿保存后发生变化。请载入当前已发布品质、重新调整并保存，以免覆盖后来发布的配置。
            </div>
          )}

          <div className="mt-4 grid grid-cols-3 gap-2">
            {(["Silver", "Gold", "Rainbow"] as const).map((tier) => (
              <div key={tier} className={`rounded-xl border px-2 py-3 text-center ${tierStyles[tier]}`}>
                <p className="text-[11px] font-bold">{tierLabels[tier]}常规池</p>
                <p className="mt-1 text-lg font-black">{regularCounts[tier]} / {REGULAR_HEXES_PER_TIER}</p>
              </div>
            ))}
          </div>
          {unbalancedPool && (
            <p className="mt-2 text-xs font-bold leading-5 text-amber-300" role="status">当前调整可以保存为草稿；发布前，每个品质必须恰好保留 18 个常规海克斯。</p>
          )}

          <div className="mt-4 flex flex-col gap-2 @[620px]:flex-row">
            <label className="min-w-0 flex-1">
              <span className="sr-only">搜索海克斯</span>
              <input
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                placeholder="按编号或名称搜索"
                className="min-h-11 w-full rounded-xl border border-gray-800 bg-gray-950 px-3 text-sm text-white outline-none focus:border-fuchsia-500"
              />
            </label>
            <label>
              <span className="sr-only">按品质筛选</span>
              <select
                value={tierFilter}
                onChange={(event) => setTierFilter(event.target.value as TierFilter)}
                className="min-h-11 w-full rounded-xl border border-gray-800 bg-gray-950 px-3 text-sm text-white outline-none focus:border-fuchsia-500 @[620px]:w-36"
              >
                <option value="all">全部品质</option>
                <option value="Silver">银色</option>
                <option value="Gold">金色</option>
                <option value="Rainbow">棱彩</option>
              </select>
            </label>
          </div>

          <div className="mt-4 grid gap-2 @[720px]:grid-cols-2">
            {visibleEntries.map((entry) => {
              const tier = tiers[entry.id] ?? entry.tier;
              const changed = tier !== entry.tier;
              return (
                <article
                  key={entry.id}
                  data-admin-hex-entry={entry.id}
                  className={`min-w-0 rounded-xl border p-3 ${changed ? "border-fuchsia-600/80 bg-fuchsia-950/25" : "border-gray-800 bg-gray-950/65"}`}
                >
                  <div className="flex min-w-0 items-start gap-3">
                    <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-gray-900 text-sm font-black text-gray-300">{entry.id}</span>
                    <div className="min-w-0 flex-1">
                      <div className="flex flex-wrap items-center gap-2">
                        <h3 className="break-words text-sm font-black text-white">{entry.name}</h3>
                        {entry.alternative && <span className="rounded-full bg-sky-900/70 px-2 py-0.5 text-[10px] font-bold text-sky-200">备用池</span>}
                        {changed && <span className="rounded-full bg-fuchsia-500/20 px-2 py-0.5 text-[10px] font-bold text-fuchsia-200">已修改</span>}
                      </div>
                      <p className="mt-1 break-words text-[11px] leading-5 text-gray-500">{entry.description}</p>
                    </div>
                  </div>
                  <label className="mt-3 flex min-h-11 items-center gap-3">
                    <span className="shrink-0 text-xs font-bold text-gray-400">草稿品质</span>
                    <select
                      aria-label={`${entry.name}草稿品质`}
                      value={tier}
                      onChange={(event) => setTiers((current) => ({
                        ...current,
                        [entry.id]: event.target.value as HexTierSnapshot,
                      }))}
                      className={`min-h-11 min-w-0 flex-1 rounded-xl border px-3 text-sm font-black outline-none focus:ring-2 focus:ring-fuchsia-400 ${tierStyles[tier]}`}
                    >
                      <option className="bg-gray-950" value="Silver">银色</option>
                      <option className="bg-gray-950" value="Gold">金色</option>
                      <option className="bg-gray-950" value="Rainbow">棱彩</option>
                    </select>
                  </label>
                  {entry.activeTier !== entry.tier && (
                    <p className="mt-2 text-[11px] text-red-300">已发布品质：{tierLabels[entry.activeTier]}；共享草稿：{tierLabels[entry.tier]}</p>
                  )}
                </article>
              );
            })}
          </div>
          {visibleEntries.length === 0 && (
            <p className="mt-4 rounded-xl border border-dashed border-gray-700 p-5 text-center text-xs text-gray-500">没有符合条件的海克斯。</p>
          )}

          <div className="mt-4 grid gap-2 @[560px]:grid-cols-3">
            <button
              type="button"
              onClick={() => setTiers(entryTierMap(selected, "draft"))}
              disabled={!dirty || pending !== null}
              className="min-h-11 rounded-xl border border-gray-700 px-3 text-sm font-bold text-gray-300 hover:bg-gray-800 disabled:cursor-not-allowed disabled:opacity-40"
            >
              撤销未保存修改
            </button>
            <button
              type="button"
              onClick={() => setTiers(entryTierMap(selected, "active"))}
              disabled={pending !== null || sortedEntries.every((entry) => (tiers[entry.id] ?? entry.tier) === entry.activeTier)}
              className="min-h-11 rounded-xl border border-emerald-800 px-3 text-sm font-bold text-emerald-300 hover:bg-emerald-950/40 disabled:cursor-not-allowed disabled:opacity-40"
            >
              载入当前已发布品质
            </button>
            <button
              type="button"
              onClick={saveDraft}
              disabled={!connected || !dirty || pending !== null}
              className="min-h-11 rounded-xl bg-fuchsia-600 px-3 text-sm font-black text-white hover:bg-fuchsia-500 disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-600"
            >
              {pending === "save" ? "正在保存草稿…" : "保存共享草稿"}
            </button>
          </div>

          <article className={`mt-4 rounded-xl border p-4 ${environment === "production" ? "border-red-900/80 bg-red-950/20" : "border-cyan-900/80 bg-cyan-950/20"}`}>
            <div className="flex flex-wrap items-start justify-between gap-2">
              <div>
                <p className={`text-sm font-black ${environment === "production" ? "text-red-200" : "text-cyan-200"}`}>
                  一键发布品质配置到{environment === "production" ? "正式服" : "测试服"}
                </p>
                <p className="mt-1 text-xs text-gray-500">{deploymentLabels[selected.deployment.state]} · {selected.deployment.message}</p>
              </div>
              <span className="rounded-full bg-gray-950 px-2.5 py-1 text-xs font-bold text-gray-400">仅配置</span>
            </div>
            <button
              type="button"
              onClick={publish}
              disabled={publishDisabled}
              className={`mt-4 min-h-11 w-full rounded-xl px-4 text-sm font-black disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-600 ${environment === "production" ? "bg-red-500 text-white hover:bg-red-400" : "bg-cyan-500 text-gray-950 hover:bg-cyan-400"}`}
            >
              {deploymentBusy || pending === "publish"
                ? "品质配置发布处理中"
                : hasApproval
                  ? `二次确认并发布到${environment === "production" ? "正式服" : "测试服"}`
                  : `申请${environment === "production" ? "正式服" : "测试服"}配置发布凭证`}
            </button>
            <p className="mt-2 text-[11px] leading-5 text-gray-500">
              必须先保存无冲突且三池平衡的草稿。此按钮不会抓取 main、部署代码或重启服务；下方“版本发布”是完全独立的整站发布流程。
            </p>
          </article>
        </>
      )}
    </section>
  );
}
