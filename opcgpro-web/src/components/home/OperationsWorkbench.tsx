"use client";

import { useEffect, useMemo, useState } from "react";
import { showMessage } from "@/components/ui/MessageBox";
import { HomeRequest } from "@/net/HomeProtocol";
import { useNetStore, type OperationsWorkbenchState } from "@/store/netStore";
import type { OperationsCaseStatus, OperationsPenaltyKind } from "@/types/net";

type WorkbenchTab = "cases" | "audit" | "doctor";

const caseStatusLabels: Record<OperationsCaseStatus, string> = {
  new: "新建",
  triaged: "已分诊",
  investigating: "调查中",
  actioned: "已处置",
  resolved: "已解决",
  rejected: "已驳回",
  appealed: "申诉中",
  closed: "已关闭",
};

const caseTransitions: Record<OperationsCaseStatus, OperationsCaseStatus[]> = {
  new: ["triaged", "rejected", "closed"],
  triaged: ["investigating", "actioned", "resolved", "rejected", "closed"],
  investigating: ["actioned", "resolved", "rejected", "closed"],
  actioned: ["resolved", "appealed", "closed"],
  resolved: ["appealed", "closed"],
  rejected: ["appealed", "closed"],
  appealed: ["investigating", "actioned", "resolved", "closed"],
  closed: [],
};

const penaltyLabels: Record<OperationsPenaltyKind, string> = {
  mute: "禁言",
  match_ban: "禁止匹配",
  spectate_chat_ban: "禁止观战与聊天",
};

function formatTimestamp(timestamp?: number | null) {
  if (!timestamp) return "—";
  return new Date(timestamp).toLocaleString("zh-CN", { hour12: false });
}

function formatDuration(milliseconds?: number | null) {
  if (milliseconds === null || milliseconds === undefined) return "暂无样本";
  if (milliseconds < 60_000) return `${Math.max(1, Math.round(milliseconds / 1_000))} 秒`;
  if (milliseconds < 3_600_000) return `${Math.round(milliseconds / 60_000)} 分钟`;
  return `${(milliseconds / 3_600_000).toFixed(1)} 小时`;
}

function compactJson(value: string) {
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}

export default function OperationsWorkbench({ previewState }: { previewState?: OperationsWorkbenchState }) {
  const liveState = useNetStore((state) => state.operationsWorkbench);
  const workbench = previewState ?? liveState;
  const [tab, setTab] = useState<WorkbenchTab>("cases");
  const [statusFilter, setStatusFilter] = useState("");
  const [sourceFilter, setSourceFilter] = useState("");
  const [accountFilter, setAccountFilter] = useState("");
  const [nextStatus, setNextStatus] = useState<OperationsCaseStatus>("triaged");
  const [assignee, setAssignee] = useState("");
  const [disposition, setDisposition] = useState("");
  const [caseNote, setCaseNote] = useState("");
  const [penaltyAccount, setPenaltyAccount] = useState("");
  const [penaltyKind, setPenaltyKind] = useState<OperationsPenaltyKind>("mute");
  const [penaltyHours, setPenaltyHours] = useState("24");
  const [penaltyReason, setPenaltyReason] = useState("");

  const selected = workbench.selectedCase;
  const availableTransitions = useMemo(
    () => selected ? caseTransitions[selected.summary.status] : [],
    [selected],
  );

  useEffect(() => {
    if (previewState) return;
    HomeRequest.requestOperationsCases();
    HomeRequest.requestPrivilegedAudit();
    HomeRequest.requestConsistencyDoctor();
  }, [previewState]);

  useEffect(() => {
    if (!selected) return;
    setNextStatus(caseTransitions[selected.summary.status][0] ?? selected.summary.status);
    setAssignee(selected.summary.assignee ?? "");
    setDisposition(selected.summary.disposition ?? "");
    setPenaltyAccount(selected.summary.subjectAccount ?? selected.summary.relatedAccount ?? "");
    setCaseNote("");
    setPenaltyReason("");
  }, [selected?.summary.caseId, selected?.summary.status, selected?.summary.updatedAt]);

  const refreshCases = () => {
    HomeRequest.requestOperationsCases({
      status: statusFilter || undefined,
      source: sourceFilter.trim() || undefined,
      account: accountFilter.trim() || undefined,
    });
  };

  const updateCase = () => {
    if (!selected || !availableTransitions.includes(nextStatus)) return;
    if (!HomeRequest.updateOperationsCase(selected.summary.caseId, nextStatus, {
      assignee: assignee.trim() || undefined,
      disposition: disposition.trim() || undefined,
      note: caseNote.trim() || undefined,
    })) showMessage("服务器未连接，Case 更新没有提交", "error");
  };

  const applyPenalty = () => {
    if (!selected) return;
    const hours = Number(penaltyHours);
    if (!Number.isFinite(hours) || hours < (1 / 60) || hours > 8_760) {
      showMessage("处罚时长必须介于 1 分钟和 365 天之间", "error");
      return;
    }
    if (!penaltyAccount.trim() || !penaltyReason.trim()) {
      showMessage("请填写处罚账号和原因", "error");
      return;
    }
    const expiresAt = Date.now() + Math.round(hours * 3_600_000);
    if (!HomeRequest.applyOperationsPenalty(
      selected.summary.caseId,
      penaltyAccount.trim(),
      penaltyKind,
      expiresAt,
      penaltyReason.trim(),
    )) showMessage("服务器未连接，处罚没有提交", "error");
  };

  const repairFinding = (findingId: number) => {
    const target = String(findingId);
    const approval = workbench.approval;
    const ready = approval?.operation === "database_repair"
      && approval.target === target
      && approval.expiresAt > Date.now();
    if (!ready) {
      if (!window.confirm(`确认申请一致性修复 #${findingId} 的一次性凭证？申请后仍需再次点击执行。`)) return;
      if (!HomeRequest.requestAdminApproval("database_repair", target)) {
        showMessage("服务器未连接，无法申请修复凭证", "error");
      }
      return;
    }
    if (!window.confirm(`即将执行一致性修复 #${findingId}。凭证只能使用一次，确认继续？`)) return;
    if (!HomeRequest.repairConsistencyFinding(findingId, approval)) {
      showMessage("服务器未连接，修复没有提交", "error");
      return;
    }
    useNetStore.getState().setAdminApproval(null);
  };

  return (
    <section
      data-operations-workbench
      aria-label="运营处置工作台"
      className="overflow-x-hidden rounded-2xl border border-indigo-900/70 bg-indigo-950/10 p-4 pb-[max(1rem,var(--layout-safe-bottom,env(safe-area-inset-bottom)))] @[640px]:p-5"
    >
      <div className="flex flex-col gap-3 @[680px]:flex-row @[680px]:items-end @[680px]:justify-between">
        <div>
          <p className="text-xs font-bold tracking-[0.16em] text-indigo-400">OPERATIONS &amp; TRUST</p>
          <h2 className="mt-1 text-lg font-black text-white">运营处置工作台</h2>
          <p className="mt-1 text-xs leading-5 text-gray-400">统一处理举报与申诉、限时处罚、不可变审计和跨库一致性异常。</p>
        </div>
        <div data-operations-workbench-tabs className="grid grid-cols-3 rounded-xl border border-gray-800 bg-gray-950 p-1">
          {(["cases", "audit", "doctor"] as const).map((value) => (
            <button
              key={value}
              type="button"
              onClick={() => setTab(value)}
              aria-pressed={tab === value}
              className={`min-h-11 rounded-lg px-3 text-sm font-bold ${tab === value ? "bg-indigo-500 text-white" : "text-gray-400 hover:bg-gray-900 hover:text-white"}`}
            >
              {value === "cases" ? "Case" : value === "audit" ? "审计" : "Doctor"}
            </button>
          ))}
        </div>
      </div>

      {tab === "cases" && (
        <div className="mt-5">
          <div className="grid grid-cols-2 gap-3 @[720px]:grid-cols-4">
            <div className="rounded-xl bg-gray-950/70 p-3"><p className="text-xs text-gray-500">Case 总数</p><p className="mt-1 text-xl font-black text-white">{workbench.metrics?.total ?? workbench.total}</p></div>
            <div className="rounded-xl bg-gray-950/70 p-3"><p className="text-xs text-gray-500">等待首次处置</p><p className="mt-1 text-xl font-black text-amber-300">{workbench.metrics?.awaitingFirstAction ?? "—"}</p></div>
            <div className="col-span-2 rounded-xl bg-gray-950/70 p-3 @[720px]:col-span-2"><p className="text-xs text-gray-500">首次处置 P90</p><p className="mt-1 text-xl font-black text-cyan-300">{formatDuration(workbench.metrics?.firstActionP90Ms)}</p></div>
          </div>

          <div data-operations-workbench-filters className="mt-4 grid gap-2 @[620px]:grid-cols-4">
            <select aria-label="按 Case 状态筛选" value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)} className="min-h-11 min-w-0 rounded-xl border border-gray-800 bg-gray-950 px-3 text-sm text-white">
              <option value="">全部状态</option>
              {Object.entries(caseStatusLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
            </select>
            <input aria-label="按来源筛选" value={sourceFilter} onChange={(event) => setSourceFilter(event.target.value)} placeholder="来源，如 player_report" className="min-h-11 min-w-0 rounded-xl border border-gray-800 bg-gray-950 px-3 text-sm text-white outline-none focus:border-indigo-500" />
            <input aria-label="按账号筛选" value={accountFilter} onChange={(event) => setAccountFilter(event.target.value)} placeholder="相关账号" className="min-h-11 min-w-0 rounded-xl border border-gray-800 bg-gray-950 px-3 text-sm text-white outline-none focus:border-indigo-500" />
            <button type="button" onClick={refreshCases} className="min-h-11 rounded-xl bg-indigo-500 px-4 text-sm font-black text-white hover:bg-indigo-400">查询 Case</button>
          </div>

          <div className="mt-4 grid min-w-0 gap-4 @[820px]:grid-cols-[minmax(16rem,0.75fr)_minmax(0,1.25fr)]">
            <div data-operations-case-list className="max-h-[34rem] min-w-0 space-y-2 overflow-y-auto pr-1">
              {workbench.cases.length ? workbench.cases.map((item) => (
                <button
                  key={item.caseId}
                  type="button"
                  onClick={() => !previewState && HomeRequest.requestOperationsCaseDetail(item.caseId)}
                  aria-pressed={selected?.summary.caseId === item.caseId}
                  className={`min-h-14 w-full min-w-0 rounded-xl border px-3 py-3 text-left ${selected?.summary.caseId === item.caseId ? "border-indigo-400 bg-indigo-950/50" : "border-gray-800 bg-gray-950/70 hover:border-gray-700"}`}
                >
                  <span className="flex min-w-0 items-start justify-between gap-2">
                    <span className="min-w-0">
                      <span className="block truncate text-sm font-black text-white">{item.title}</span>
                      <span className="mt-1 block break-all text-[11px] leading-4 text-gray-500">{item.caseId} · {item.source}</span>
                    </span>
                    <span className="shrink-0 rounded-full bg-indigo-500/15 px-2 py-1 text-[11px] font-bold text-indigo-200">{caseStatusLabels[item.status]}</span>
                  </span>
                  <span className="mt-2 block text-xs text-gray-400">账号 {item.subjectAccount ?? item.relatedAccount ?? item.reporterAccount ?? "—"} · 证据 {item.evidenceCount} · 生效处罚 {item.activePenaltyCount}</span>
                </button>
              )) : <div className="rounded-xl border border-dashed border-gray-800 px-4 py-10 text-center text-sm text-gray-600">当前筛选条件下没有 Case</div>}
            </div>

            <div data-operations-case-detail className="min-w-0 rounded-xl border border-gray-800 bg-gray-950/70 p-4">
              {selected ? (
                <div className="min-w-0">
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="min-w-0"><p className="break-words text-base font-black text-white">{selected.summary.title}</p><p className="mt-1 break-all text-xs text-gray-500">{selected.summary.caseId} · 创建于 {formatTimestamp(selected.summary.createdAt)}</p></div>
                    <span className="rounded-full bg-indigo-500/15 px-2.5 py-1 text-xs font-bold text-indigo-200">{caseStatusLabels[selected.summary.status]}</span>
                  </div>
                  <p className="mt-3 whitespace-pre-wrap break-words text-sm leading-6 text-gray-300">{selected.description}</p>
                  <dl className="mt-3 grid grid-cols-2 gap-2 text-xs">
                    <div className="rounded-lg bg-gray-900 p-2"><dt className="text-gray-600">举报 / 发起</dt><dd className="mt-1 break-all text-gray-300">{selected.summary.reporterAccount ?? "—"}</dd></div>
                    <div className="rounded-lg bg-gray-900 p-2"><dt className="text-gray-600">被举报 / 对象</dt><dd className="mt-1 break-all text-gray-300">{selected.summary.subjectAccount ?? selected.summary.relatedAccount ?? "—"}</dd></div>
                    <div className="rounded-lg bg-gray-900 p-2"><dt className="text-gray-600">房间</dt><dd className="mt-1 break-all text-gray-300">{selected.summary.roomId ?? "—"}</dd></div>
                    <div className="rounded-lg bg-gray-900 p-2"><dt className="text-gray-600">回放</dt><dd className="mt-1 break-all text-gray-300">{selected.summary.replayId ?? "—"}</dd></div>
                  </dl>

                  <div className="mt-4 border-t border-gray-800 pt-4">
                    <p className="text-xs font-black tracking-[0.12em] text-indigo-300">流转与处置</p>
                    <div className="mt-2 grid gap-2 @[620px]:grid-cols-2">
                      <select aria-label="Case 下一状态" value={nextStatus} onChange={(event) => setNextStatus(event.target.value as OperationsCaseStatus)} disabled={!availableTransitions.length} className="min-h-11 min-w-0 rounded-xl border border-gray-800 bg-gray-900 px-3 text-sm text-white disabled:opacity-50">
                        {availableTransitions.length ? availableTransitions.map((status) => <option key={status} value={status}>{caseStatusLabels[status]}</option>) : <option value={selected.summary.status}>没有后续状态</option>}
                      </select>
                      <input aria-label="Case 负责人" value={assignee} onChange={(event) => setAssignee(event.target.value)} placeholder="负责人账号" className="min-h-11 min-w-0 rounded-xl border border-gray-800 bg-gray-900 px-3 text-sm text-white" />
                      <input aria-label="Case 处置结论" value={disposition} onChange={(event) => setDisposition(event.target.value)} placeholder="处置结论" className="min-h-11 min-w-0 rounded-xl border border-gray-800 bg-gray-900 px-3 text-sm text-white" />
                      <input aria-label="Case 操作备注" value={caseNote} onChange={(event) => setCaseNote(event.target.value)} placeholder="本次操作备注" className="min-h-11 min-w-0 rounded-xl border border-gray-800 bg-gray-900 px-3 text-sm text-white" />
                    </div>
                    <button type="button" onClick={updateCase} disabled={!availableTransitions.length || Boolean(previewState)} className="mt-2 min-h-11 w-full rounded-xl bg-indigo-500 px-4 text-sm font-black text-white hover:bg-indigo-400 disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-600">提交状态流转</button>
                  </div>

                  <div className="mt-4 border-t border-gray-800 pt-4">
                    <p className="text-xs font-black tracking-[0.12em] text-red-300">限时处罚</p>
                    <div className="mt-2 grid gap-2 @[620px]:grid-cols-2">
                      <input aria-label="处罚账号" value={penaltyAccount} onChange={(event) => setPenaltyAccount(event.target.value)} placeholder="处罚账号" className="min-h-11 min-w-0 rounded-xl border border-gray-800 bg-gray-900 px-3 text-sm text-white" />
                      <select aria-label="处罚类型" value={penaltyKind} onChange={(event) => setPenaltyKind(event.target.value as OperationsPenaltyKind)} className="min-h-11 min-w-0 rounded-xl border border-gray-800 bg-gray-900 px-3 text-sm text-white">{Object.entries(penaltyLabels).map(([kind, label]) => <option key={kind} value={kind}>{label}</option>)}</select>
                      <input aria-label="处罚时长小时" type="number" min="0.0167" max="8760" step="0.5" value={penaltyHours} onChange={(event) => setPenaltyHours(event.target.value)} className="min-h-11 min-w-0 rounded-xl border border-gray-800 bg-gray-900 px-3 text-sm text-white" />
                      <input aria-label="处罚原因" value={penaltyReason} onChange={(event) => setPenaltyReason(event.target.value)} placeholder="处罚原因（必填）" className="min-h-11 min-w-0 rounded-xl border border-gray-800 bg-gray-900 px-3 text-sm text-white" />
                    </div>
                    <button type="button" onClick={applyPenalty} disabled={Boolean(previewState)} className="mt-2 min-h-11 w-full rounded-xl border border-red-700 bg-red-950/30 px-4 text-sm font-black text-red-200 hover:bg-red-950/60 disabled:opacity-50">执行限时处罚</button>
                    <div className="mt-3 space-y-2">
                      {selected.penalties.map((penalty) => (
                        <div key={penalty.penaltyId} className="flex flex-col gap-2 rounded-lg bg-gray-900 p-3 @[600px]:flex-row @[600px]:items-center @[600px]:justify-between">
                          <div className="min-w-0 text-xs leading-5"><p className="break-all font-bold text-gray-200">{penaltyLabels[penalty.kind]} · {penalty.account}</p><p className="break-words text-gray-500">至 {formatTimestamp(penalty.expiresAt)} · {penalty.reason}</p></div>
                          {!penalty.revokedAt && penalty.expiresAt > Date.now() && <button type="button" disabled={Boolean(previewState)} onClick={() => HomeRequest.revokeOperationsPenalty(penalty.penaltyId, "管理员手动撤销")} className="min-h-11 shrink-0 rounded-lg border border-gray-700 px-3 text-xs font-bold text-gray-300 disabled:opacity-50">撤销</button>}
                        </div>
                      ))}
                    </div>
                  </div>

                  <details className="mt-4 border-t border-gray-800 pt-4">
                    <summary className="min-h-11 cursor-pointer py-3 text-sm font-bold text-gray-300">证据与时间线（{selected.evidence.length + selected.events.length}）</summary>
                    <div className="space-y-2">
                      {selected.evidence.map((evidence) => <pre key={`e-${evidence.id}`} className="max-h-48 overflow-auto whitespace-pre-wrap break-all rounded-lg bg-black/40 p-3 text-[11px] leading-5 text-gray-400">{evidence.type} · {formatTimestamp(evidence.createdAt)}{evidence.expiresAt ? ` · 保留至 ${formatTimestamp(evidence.expiresAt)}` : ""}{"\n"}{compactJson(evidence.payloadJson)}</pre>)}
                      {selected.events.map((event) => <div key={`v-${event.id}`} className="rounded-lg bg-gray-900 p-3 text-xs leading-5 text-gray-400"><p className="font-bold text-gray-200">{event.eventType} · {event.actorAccount}</p><p>{event.fromStatus ?? "—"} → {event.toStatus ?? "—"} · {formatTimestamp(event.createdAt)}</p>{event.note && <p className="break-words">{event.note}</p>}</div>)}
                    </div>
                  </details>
                </div>
              ) : <div className="flex min-h-64 items-center justify-center px-4 text-center text-sm leading-6 text-gray-600">从左侧选择 Case 后，可查看证据链、流转状态和限时处罚。</div>}
            </div>
          </div>
        </div>
      )}

      {tab === "audit" && (
        <div data-operations-audit className="mt-5">
          <div className="flex flex-col gap-3 @[560px]:flex-row @[560px]:items-center @[560px]:justify-between">
            <div><p className={`text-sm font-black ${workbench.auditChainValid ? "text-emerald-300" : "text-red-300"}`}>{workbench.auditChainValid === null ? "审计链尚未校验" : workbench.auditChainValid ? "审计哈希链完整" : "审计哈希链异常"}</p><p className="mt-1 text-xs text-gray-500">共显示 {workbench.auditEntries.length} 条特权操作；数据库拒绝更新与删除既有条目。</p></div>
            <button type="button" disabled={Boolean(previewState)} onClick={() => HomeRequest.requestPrivilegedAudit()} className="min-h-11 rounded-xl border border-gray-700 px-4 text-sm font-bold text-gray-200 disabled:opacity-50">重新校验</button>
          </div>
          <div className="mt-4 space-y-2">
            {workbench.auditEntries.map((entry) => (
              <article key={entry.id} className="min-w-0 rounded-xl border border-gray-800 bg-gray-950/70 p-3">
                <div className="flex flex-wrap items-start justify-between gap-2"><p className="break-all text-sm font-black text-white">{entry.operation} · {entry.target ?? "全局"}</p><span className={`rounded-full px-2 py-1 text-[11px] font-bold ${entry.result === "success" ? "bg-emerald-500/15 text-emerald-300" : entry.result === "failed" ? "bg-red-500/15 text-red-300" : "bg-amber-500/15 text-amber-300"}`}>{entry.result}</span></div>
                <p className="mt-1 break-all text-xs text-gray-500">{entry.actorAccount} · {entry.source} · {formatTimestamp(entry.createdAt)} · 请求 {entry.requestId}</p>
                <pre className="mt-2 max-h-32 overflow-auto whitespace-pre-wrap break-all rounded-lg bg-black/40 p-2 text-[11px] leading-5 text-gray-500">{compactJson(entry.detailJson)}</pre>
              </article>
            ))}
          </div>
        </div>
      )}

      {tab === "doctor" && (
        <div data-operations-doctor className="mt-5">
          <div className="flex flex-col gap-3 @[560px]:flex-row @[560px]:items-center @[560px]:justify-between">
            <div><p className="text-sm font-black text-white">最近巡检：{formatTimestamp(workbench.doctorSnapshot?.checkedAt)}</p><p className="mt-1 text-xs text-gray-500">待处理异常 {workbench.doctorSnapshot?.openFindings ?? workbench.findings.length} · 本轮成功 {workbench.doctorSnapshot?.succeeded ?? 0} · 重试 {workbench.doctorSnapshot?.retried ?? 0}</p></div>
            <button type="button" disabled={Boolean(previewState)} onClick={() => HomeRequest.requestConsistencyDoctor()} className="min-h-11 rounded-xl bg-cyan-500 px-4 text-sm font-black text-gray-950 disabled:opacity-50">立即巡检</button>
          </div>
          <div className="mt-4 grid gap-2 @[700px]:grid-cols-3">
            {workbench.doctorSnapshot?.schemas.map((schema) => (
              <article key={schema.name} className="min-w-0 rounded-xl border border-gray-800 bg-gray-950/70 p-3">
                <div className="flex items-center justify-between gap-2"><p className="truncate text-sm font-black text-white">{schema.name}</p><span className={`rounded-full px-2 py-1 text-[11px] font-bold ${schema.healthy ? "bg-emerald-500/15 text-emerald-300" : "bg-red-500/15 text-red-300"}`}>{schema.healthy ? "健康" : "异常"}</span></div>
                <p className="mt-2 break-all text-[11px] leading-5 text-gray-500">{schema.path}</p><p className="mt-1 text-xs text-gray-400">quick_check: {schema.integrity} · user_version {schema.userVersion}</p>
              </article>
            ))}
          </div>
          <div className="mt-4 space-y-2">
            {workbench.findings.map((finding) => {
              const approvalReady = workbench.approval?.operation === "database_repair"
                && workbench.approval.target === String(finding.id)
                && workbench.approval.expiresAt > Date.now();
              return (
                <article key={finding.id} className="min-w-0 rounded-xl border border-amber-900/70 bg-amber-950/10 p-3">
                  <div className="flex flex-col gap-3 @[620px]:flex-row @[620px]:items-start @[620px]:justify-between">
                    <div className="min-w-0"><p className="break-all text-sm font-black text-amber-200">#{finding.id} · {finding.scope} · {finding.findingKey}</p><p className="mt-1 text-xs text-gray-500">{finding.severity} · {finding.status} · {finding.repairAction}</p>{finding.lastError && <p className="mt-1 break-words text-xs text-red-300">{finding.lastError}</p>}</div>
                    <button type="button" disabled={Boolean(previewState)} onClick={() => repairFinding(finding.id)} className={`min-h-11 shrink-0 rounded-xl px-4 text-sm font-black disabled:opacity-50 ${approvalReady ? "bg-red-500 text-white" : "border border-amber-700 text-amber-200"}`}>{approvalReady ? "二次确认并修复" : "申请修复凭证"}</button>
                  </div>
                  <details className="mt-2"><summary className="min-h-11 cursor-pointer py-3 text-xs font-bold text-gray-400">查看权威值与观测值</summary><div className="grid gap-2 @[620px]:grid-cols-2"><pre className="overflow-auto whitespace-pre-wrap break-all rounded-lg bg-black/40 p-2 text-[11px] text-gray-500">权威值{"\n"}{compactJson(finding.authoritativeJson)}</pre><pre className="overflow-auto whitespace-pre-wrap break-all rounded-lg bg-black/40 p-2 text-[11px] text-gray-500">观测值{"\n"}{compactJson(finding.observedJson)}</pre></div></details>
                </article>
              );
            })}
            {!workbench.findings.length && <div className="rounded-xl border border-dashed border-gray-800 px-4 py-10 text-center text-sm text-emerald-300">当前没有未解决的一致性异常</div>}
          </div>
        </div>
      )}
    </section>
  );
}
