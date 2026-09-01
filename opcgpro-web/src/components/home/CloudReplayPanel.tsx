"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { importReplayDocument } from "@/data/matchHistoryDB";
import { rememberCloudReplayLink } from "@/data/cloudReplayLink";
import { getCard } from "@/data/CardLoader";
import { HomeRequest } from "@/net/HomeProtocol";
import { eventBus } from "@/net/eventBus";
import type {
  CloudReplayListItem,
  CloudReplayOutcome,
  CloudReplaySharePolicy,
  MsgBase,
  MsgCloudReplayBookmark,
  MsgCloudReplayDelete,
  MsgCloudReplayList,
  MsgCloudReplayLoad,
  MsgCloudReplayShare,
} from "@/types/net";

interface Props {
  onShowLocal: () => void;
  /** 仅供真实浏览器布局回归使用；正常大厅不传入。 */
  previewItems?: CloudReplayListItem[];
}

type CloudMutationProto =
  | "MsgCloudReplayBookmark"
  | "MsgCloudReplayShare"
  | "MsgCloudReplayDelete";

interface PendingCloudMutation {
  proto: CloudMutationProto;
  requestId: string;
  replayId: string;
  timeoutId: number | null;
}

let requestSequence = 0;
function requestId(operation: string) {
  requestSequence += 1;
  return `cloud-${operation}-${Date.now()}-${requestSequence}`;
}

function leaderLabel(number: string) {
  return getCard(number)?.name || number || "—";
}

function formatBytes(bytes: number) {
  if (!Number.isFinite(bytes) || bytes <= 0) return "0 MB";
  return `${(bytes / 1024 / 1024).toFixed(bytes >= 10 * 1024 * 1024 ? 0 : 1)} MB`;
}

function formatTime(timestamp: number) {
  try {
    return new Date(timestamp).toLocaleString("zh-CN", {
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
    });
  } catch {
    return "";
  }
}

const POLICY_LABELS: Record<CloudReplaySharePolicy, string> = {
  masked: "全程隐藏手牌",
  final_hands: "仅公开终局手牌",
  full_timeline: "公开完整手牌时间线",
};

export default function CloudReplayPanel({ onShowLocal, previewItems }: Props) {
  const router = useRouter();
  const previewMode = previewItems !== undefined;
  const [items, setItems] = useState<CloudReplayListItem[]>(previewItems ?? []);
  const [loading, setLoading] = useState(!previewMode);
  const [pendingReplay, setPendingReplay] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<{ type: "success" | "error"; text: string } | null>(null);
  const [opponent, setOpponent] = useState("");
  const [outcome, setOutcome] = useState<"" | CloudReplayOutcome>("");
  const [matchKind, setMatchKind] = useState("");
  const [bookmarkedOnly, setBookmarkedOnly] = useState(false);
  const [usedBytes, setUsedBytes] = useState(previewMode ? 18 * 1024 * 1024 : 0);
  const [quotaBytes, setQuotaBytes] = useState(previewMode ? 256 * 1024 * 1024 : 0);
  const [retentionDays, setRetentionDays] = useState(90);
  const [maximumReplays, setMaximumReplays] = useState(100);
  const [sharePolicies, setSharePolicies] = useState<Record<string, CloudReplaySharePolicy>>({});
  const [lastShareToken, setLastShareToken] = useState<string | null>(null);
  const [sharedReplayId, setSharedReplayId] = useState("");
  const [sharedToken, setSharedToken] = useState("");
  const latestListRequest = useRef<string | null>(null);
  const latestLoadRequest = useRef<string | null>(null);
  const latestMutationRequest = useRef<PendingCloudMutation | null>(null);

  const refresh = useCallback(() => {
    const id = requestId("list");
    latestListRequest.current = id;
    setLoading(true);
    setFeedback(null);
    const sent = HomeRequest.requestCloudReplays({
      requestId: id,
      opponent: opponent.trim() || undefined,
      outcome: outcome || undefined,
      matchKind: matchKind || undefined,
      bookmarkedOnly,
      offset: 0,
      limit: 50,
    });
    if (!sent) {
      latestListRequest.current = null;
      setLoading(false);
      setFeedback({ type: "error", text: "网络未连接，无法读取云回放。" });
      return;
    }
    window.setTimeout(() => {
      if (latestListRequest.current !== id) return;
      latestListRequest.current = null;
      setLoading(false);
      setFeedback({ type: "error", text: "云回放列表请求超时，请重试。" });
    }, 8_000);
  }, [bookmarkedOnly, matchKind, opponent, outcome]);

  useEffect(() => {
    if (!previewMode) refresh();
  }, []); // 首次进入只请求一次；筛选由玩家点击应用。

  useEffect(() => {
    if (previewMode) return;
    const handler = async (base: MsgBase) => {
      if (base.proto === "MsgCloudReplayList") {
        const message = base as MsgCloudReplayList;
        if (message.requestId !== latestListRequest.current) return;
        latestListRequest.current = null;
        setLoading(false);
        if (message.result !== true) {
          setFeedback({ type: "error", text: message.logStr || "读取云回放失败。" });
          return;
        }
        const nextItems = Array.isArray(message.items) ? message.items : [];
        setItems(nextItems);
        setUsedBytes(Math.max(0, message.usedBytes || 0));
        setQuotaBytes(Math.max(0, message.quotaBytes || 0));
        setRetentionDays(Math.max(1, message.retentionDays || 90));
        setMaximumReplays(Math.max(1, message.maximumReplays || 100));
        setSharePolicies((current) => Object.fromEntries(nextItems.map((item) => [
          item.replayId,
          current[item.replayId] || item.sharePolicy || "masked",
        ])));
        return;
      }

      if (base.proto === "MsgCloudReplayLoad") {
        const message = base as MsgCloudReplayLoad;
        if (message.requestId !== latestLoadRequest.current) return;
        latestLoadRequest.current = null;
        setPendingReplay(null);
        if (message.result !== true || !message.document) {
          const text = message.errorCode === "runtime_missing"
            ? message.logStr || "这局所需的历史运行时尚未归档，暂时无法打开。"
            : message.logStr || "读取云回放失败。";
          setFeedback({ type: "error", text });
          return;
        }
        try {
          const imported = await importReplayDocument(message.document);
          rememberCloudReplayLink(imported.id, message.replayId);
          router.push(`/replay/${encodeURIComponent(imported.id)}`);
        } catch {
          setFeedback({ type: "error", text: "云回放内容校验失败，未写入本机历史。" });
        }
        return;
      }

      if (base.proto === "MsgCloudReplayBookmark") {
        const message = base as MsgCloudReplayBookmark;
        const pending = latestMutationRequest.current;
        if (!pending
            || pending.proto !== base.proto
            || pending.requestId !== message.requestId
            || pending.replayId !== message.replayId) return;
        if (pending.timeoutId !== null) window.clearTimeout(pending.timeoutId);
        latestMutationRequest.current = null;
        setPendingReplay(null);
        if (message.result !== true) {
          setFeedback({ type: "error", text: message.logStr || "更新书签失败。" });
          return;
        }
        setItems((current) => current.map((item) => item.replayId === message.replayId
          ? { ...item, bookmarked: message.bookmarked }
          : item));
        return;
      }

      if (base.proto === "MsgCloudReplayShare") {
        const message = base as MsgCloudReplayShare;
        const pending = latestMutationRequest.current;
        if (!pending
            || pending.proto !== base.proto
            || pending.requestId !== message.requestId
            || pending.replayId !== message.replayId) return;
        if (pending.timeoutId !== null) window.clearTimeout(pending.timeoutId);
        latestMutationRequest.current = null;
        setPendingReplay(null);
        if (message.result !== true) {
          setFeedback({ type: "error", text: message.logStr || "更新分享设置失败。" });
          return;
        }
        setItems((current) => current.map((item) => item.replayId === message.replayId
          ? { ...item, shared: message.shared === true, sharePolicy: message.sharePolicy }
          : item));
        if (message.shareToken) {
          setLastShareToken(message.shareToken);
          try {
            await navigator.clipboard.writeText(`${message.replayId}.${message.shareToken}`);
            setFeedback({ type: "success", text: "分享凭证已复制。它只会显示这一次，请妥善保存。" });
          } catch {
            setFeedback({ type: "success", text: "分享已开启，请手动复制下方一次性显示的凭证。" });
          }
        } else {
          setLastShareToken(null);
          setFeedback({ type: "success", text: "分享已关闭，旧凭证立即失效。" });
        }
        return;
      }

      if (base.proto === "MsgCloudReplayDelete") {
        const message = base as MsgCloudReplayDelete;
        const pending = latestMutationRequest.current;
        if (!pending
            || pending.proto !== base.proto
            || pending.requestId !== message.requestId
            || pending.replayId !== message.replayId) return;
        if (pending.timeoutId !== null) window.clearTimeout(pending.timeoutId);
        latestMutationRequest.current = null;
        setPendingReplay(null);
        if (message.result !== true) {
          setFeedback({ type: "error", text: message.logStr || "删除云回放失败。" });
          return;
        }
        setItems((current) => current.filter((item) => item.replayId !== message.replayId));
        setFeedback({ type: "success", text: "云回放已永久删除。" });
      }
    };
    eventBus.on("message", handler);
    return () => {
      eventBus.off("message", handler);
      latestListRequest.current = null;
      latestLoadRequest.current = null;
      const pending = latestMutationRequest.current;
      if (pending?.timeoutId != null) window.clearTimeout(pending.timeoutId);
      latestMutationRequest.current = null;
    };
  }, [previewMode, router]);

  function beginMutation(
    operation: string,
    proto: CloudMutationProto,
    replayId: string,
    send: (id: string) => boolean,
    timeoutMessage: string,
  ) {
    if (pendingReplay) return false;
    const id = requestId(operation);
    const pending: PendingCloudMutation = { proto, requestId: id, replayId, timeoutId: null };
    latestMutationRequest.current = pending;
    setPendingReplay(replayId);
    setFeedback(null);
    if (!send(id)) {
      latestMutationRequest.current = null;
      setPendingReplay(null);
      return false;
    }
    pending.timeoutId = window.setTimeout(() => {
      if (latestMutationRequest.current !== pending) return;
      latestMutationRequest.current = null;
      setPendingReplay(null);
      setFeedback({ type: "error", text: timeoutMessage });
    }, 10_000);
    return true;
  }

  function loadReplay(replayId: string, shareToken?: string) {
    if (pendingReplay) return;
    const id = requestId("load");
    latestLoadRequest.current = id;
    setPendingReplay(replayId);
    setFeedback(null);
    if (!HomeRequest.loadCloudReplay(id, replayId, shareToken)) {
      latestLoadRequest.current = null;
      setPendingReplay(null);
      setFeedback({ type: "error", text: "网络未连接，无法读取云回放。" });
      return;
    }
    window.setTimeout(() => {
      if (latestLoadRequest.current !== id) return;
      latestLoadRequest.current = null;
      setPendingReplay(null);
      setFeedback({ type: "error", text: "云回放读取超时，请重试。" });
    }, 12_000);
  }

  function loadSharedReplay() {
    const replayId = sharedReplayId.trim();
    const token = sharedToken.trim();
    if (!replayId || !token) {
      setFeedback({ type: "error", text: "请输入回放 ID 和分享凭证。" });
      return;
    }
    loadReplay(replayId, token);
  }

  function toggleBookmark(item: CloudReplayListItem) {
    if (pendingReplay) return;
    if (!beginMutation(
      "bookmark",
      "MsgCloudReplayBookmark",
      item.replayId,
      (id) => HomeRequest.bookmarkCloudReplay(id, item.replayId, !item.bookmarked),
      "更新书签超时，请重试。",
    )) {
      setFeedback({ type: "error", text: "网络未连接，无法更新书签。" });
    }
  }

  function toggleShare(item: CloudReplayListItem) {
    if (pendingReplay) return;
    const policy = sharePolicies[item.replayId] || "masked";
    if (!item.shared && policy === "full_timeline"
        && !confirm("完整时间线会公开双方整局手牌变化。确认生成新的分享凭证？")) return;
    if (!beginMutation(
      "share",
      "MsgCloudReplayShare",
      item.replayId,
      (id) => HomeRequest.shareCloudReplay(id, item.replayId, !item.shared, policy),
      "更新分享设置超时，请重试。",
    )) {
      setFeedback({ type: "error", text: "网络未连接，无法更新分享设置。" });
    }
  }

  function deleteReplay(item: CloudReplayListItem) {
    if (pendingReplay || !confirm("确定永久删除这份云回放？删除后无法恢复，已生成的分享凭证也会失效。")) return;
    if (!beginMutation(
      "delete",
      "MsgCloudReplayDelete",
      item.replayId,
      (id) => HomeRequest.deleteCloudReplay(id, item.replayId),
      "删除云回放超时，请重试。",
    )) {
      setFeedback({ type: "error", text: "网络未连接，无法删除云回放。" });
    }
  }

  return (
    <div data-cloud-replay-panel className="flex h-full min-h-0 flex-col p-3 @[640px]:p-6">
      <div className="mb-3 flex flex-wrap items-start gap-3">
        <div className="min-w-0 flex-1">
          <h2 className="text-xl font-bold text-white">对局记录</h2>
          <div className="mt-2 inline-flex rounded-lg border border-gray-700 bg-gray-950 p-1" role="tablist" aria-label="记录来源">
            <button type="button" role="tab" aria-selected="false" onClick={onShowLocal} className="min-h-11 rounded-md px-4 text-sm text-gray-400">本机</button>
            <button type="button" role="tab" aria-selected="true" className="min-h-11 rounded-md bg-orange-500 px-4 text-sm font-bold text-white">云端</button>
          </div>
        </div>
        <p className="max-w-full text-right text-xs leading-5 text-gray-500">
          已用 {formatBytes(usedBytes)} / {formatBytes(quotaBytes)}<br />
          默认保留 {retentionDays} 天、最近 {maximumReplays} 局；书签免普通清理但仍受硬配额限制
        </p>
      </div>

      <div data-cloud-replay-filters className="mb-3 grid grid-cols-1 gap-2 rounded-xl border border-gray-800 bg-gray-900/70 p-3 @[560px]:grid-cols-2 @[900px]:grid-cols-[minmax(0,1fr)_auto_auto_auto_auto]">
        <input value={opponent} onChange={(event) => setOpponent(event.target.value)} maxLength={80} placeholder="筛选对手昵称" className="min-h-11 min-w-0 rounded-lg border border-gray-700 bg-gray-950 px-3 text-sm text-white outline-none focus:border-orange-500" />
        <select value={outcome} onChange={(event) => setOutcome(event.target.value as "" | CloudReplayOutcome)} className="min-h-11 rounded-lg border border-gray-700 bg-gray-950 px-3 text-sm text-white">
          <option value="">全部结果</option><option value="win">胜</option><option value="loss">负</option><option value="draw">平</option>
        </select>
        <select value={matchKind} onChange={(event) => setMatchKind(event.target.value)} className="min-h-11 rounded-lg border border-gray-700 bg-gray-950 px-3 text-sm text-white">
          <option value="">全部模式</option><option value="Ranked">标准排位</option><option value="RankedWild">狂野排位</option><option value="CasualStandard">标准休闲</option><option value="Casual">狂野休闲</option><option value="Hex">海克斯</option><option value="Friendly">友谊战</option><option value="Bot">AI 对战</option>
        </select>
        <label className="flex min-h-11 items-center gap-2 rounded-lg border border-gray-700 px-3 text-sm text-gray-300"><input type="checkbox" checked={bookmarkedOnly} onChange={(event) => setBookmarkedOnly(event.target.checked)} />仅书签</label>
        <button type="button" onClick={refresh} className="min-h-11 rounded-lg bg-orange-500 px-4 text-sm font-bold text-white hover:bg-orange-400">应用筛选</button>
      </div>

      <div data-cloud-replay-shared-access className="mb-3 grid grid-cols-1 gap-2 rounded-xl border border-sky-700/40 bg-sky-950/20 p-3 @[640px]:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto]">
        <input value={sharedReplayId} onChange={(event) => setSharedReplayId(event.target.value)} maxLength={64} placeholder="别人分享的回放 ID" className="min-h-11 min-w-0 rounded-lg border border-sky-800 bg-gray-950 px-3 text-sm text-white" />
        <input value={sharedToken} onChange={(event) => setSharedToken(event.target.value)} maxLength={64} placeholder="分享凭证" className="min-h-11 min-w-0 rounded-lg border border-sky-800 bg-gray-950 px-3 text-sm text-white" />
        <button type="button" onClick={loadSharedReplay} disabled={pendingReplay !== null} className="min-h-11 rounded-lg border border-sky-500 px-4 text-sm font-bold text-sky-200 disabled:opacity-50">打开分享</button>
      </div>

      <div className="min-h-6" aria-live="polite">
        {feedback && <p role={feedback.type === "error" ? "alert" : "status"} className={`mb-2 break-words text-xs ${feedback.type === "error" ? "text-red-400" : "text-emerald-400"}`}>{feedback.text}</p>}
        {lastShareToken && <code className="mb-2 block max-w-full select-all overflow-x-auto rounded bg-black/40 p-2 text-xs text-amber-200">{lastShareToken}</code>}
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain">
        {loading ? <p className="py-12 text-center text-sm text-gray-500">加载云回放中…</p>
          : items.length === 0 ? <p className="py-12 text-center text-sm text-gray-500">暂无符合条件的新完成对局；旧本机记录不会自动上传</p>
          : <ul className="flex flex-col gap-2">
            {items.map((item) => (
              <li data-cloud-replay-item key={item.replayId} className="rounded-xl border border-gray-800 bg-gray-900 p-3 hover:border-orange-600/60">
                <div className="flex min-w-0 flex-wrap items-center gap-2">
                  <button type="button" disabled={pendingReplay !== null} onClick={() => loadReplay(item.replayId)} className="flex min-h-11 min-w-0 flex-1 items-center gap-3 text-left disabled:opacity-50">
                    <span className={`rounded-md px-2 py-1 text-xs font-bold ${item.isDraw ? "bg-sky-500/20 text-sky-300" : item.winnerIsMe ? "bg-yellow-500/20 text-yellow-300" : "bg-gray-700 text-gray-300"}`}>{item.isDraw ? "平" : item.winnerIsMe ? "胜" : "负"}</span>
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-sm text-white"><span className="text-sky-300">{leaderLabel(item.myLeader)}</span><span className="mx-1.5 text-gray-600">vs</span><span className="text-red-300">{leaderLabel(item.opponentLeader)}</span></span>
                      <span className="mt-0.5 block truncate text-xs text-gray-500">对手 {item.opponentName || "—"} · {item.turnCount} 回合 · {formatTime(item.startedAt)}</span>
                    </span>
                    <span className="text-sm text-orange-300">{pendingReplay === item.replayId ? "读取中…" : "▶ 回放"}</span>
                  </button>
                  <button type="button" aria-label={item.bookmarked ? "取消书签" : "添加书签"} onClick={() => toggleBookmark(item)} disabled={pendingReplay !== null} className="flex h-11 min-w-11 items-center justify-center rounded-lg border border-gray-700 text-lg text-amber-300 disabled:opacity-50">{item.bookmarked ? "★" : "☆"}</button>
                  <button type="button" aria-label="永久删除云回放" onClick={() => deleteReplay(item)} disabled={pendingReplay !== null} className="flex h-11 min-w-11 items-center justify-center rounded-lg border border-gray-700 text-red-400 disabled:opacity-50">✕</button>
                </div>
                <div className="mt-2 grid grid-cols-1 gap-2 border-t border-gray-800 pt-2 @[640px]:grid-cols-[minmax(0,1fr)_auto]">
                  <select aria-label="分享手牌策略" value={sharePolicies[item.replayId] || item.sharePolicy} disabled={item.shared || pendingReplay !== null} onChange={(event) => setSharePolicies((current) => ({ ...current, [item.replayId]: event.target.value as CloudReplaySharePolicy }))} className="min-h-11 min-w-0 rounded-lg border border-gray-700 bg-gray-950 px-3 text-sm text-gray-200 disabled:opacity-60">
                    {(Object.keys(POLICY_LABELS) as CloudReplaySharePolicy[]).map((policy) => <option key={policy} value={policy}>{POLICY_LABELS[policy]}</option>)}
                  </select>
                  <button type="button" onClick={() => toggleShare(item)} disabled={pendingReplay !== null} className={`min-h-11 rounded-lg border px-4 text-sm font-bold disabled:opacity-50 ${item.shared ? "border-red-700 text-red-300" : "border-emerald-700 text-emerald-300"}`}>{item.shared ? "关闭分享" : "生成分享凭证"}</button>
                </div>
                <p className="mt-2 text-[11px] text-gray-600">{formatBytes(item.sizeBytes)} · {item.feedbackCount > 0 ? `已关联 ${item.feedbackCount} 条反馈 · ` : ""}{item.shared ? `分享中：${POLICY_LABELS[item.sharePolicy]}` : "未分享"}</p>
              </li>
            ))}
          </ul>}
      </div>
    </div>
  );
}
