"use client";

import { useEffect, useState, useCallback, useRef, type ChangeEvent } from "react";
import { useRouter } from "next/navigation";
import {
  listMeta,
  deleteMatch,
  clearAll,
  getSnapshots,
  importReplayDocument,
  type MatchMeta,
} from "@/data/matchHistoryDB";
import {
  ReplayFileError,
  createReplayDocument,
  createReplayFilename,
  parseReplayText,
  serializeReplayDocument,
  validateReplayFileSize,
} from "@/data/matchReplayFile";
import { getCard } from "@/data/CardLoader";
import { getMatchOpeningLabels } from "@/data/matchHistoryOpening";
import { useLanguage } from "@/i18n/LanguageProvider";
import CloudReplayPanel from "./CloudReplayPanel";

function fmtTime(ts: number, locale: string): string {
  try {
    return new Date(ts).toLocaleString(locale, {
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
    });
  } catch {
    return "";
  }
}

function leaderLabel(num: string): string {
  if (!num) return "—";
  const card = getCard(num);
  return card?.name ? card.name : num;
}

export default function HistoryPanel() {
  const { locale, t } = useLanguage();
  const router = useRouter();
  const [list, setList] = useState<MatchMeta[]>([]);
  const [loading, setLoading] = useState(true);
  const [importing, setImporting] = useState(false);
  const [exportingId, setExportingId] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<{ type: "success" | "error"; text: string } | null>(null);
  const [source, setSource] = useState<"local" | "cloud">("local");
  const fileInputRef = useRef<HTMLInputElement>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    try {
      setList(await listMeta());
    } catch {
      setList([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    refresh();
  }, [refresh]);

  const handleDelete = async (id: string) => {
    await deleteMatch(id).catch(() => {});
    refresh();
  };

  const handleClear = async () => {
    if (!confirm(t("确定清空全部对局记录？此操作不可恢复。"))) return;
    await clearAll().catch(() => {});
    refresh();
  };

  const handleChooseImport = () => {
    if (importing) return;
    setFeedback(null);
    if (fileInputRef.current) {
      // 清空 value 后，同一个文件无需改名即可连续选择。
      fileInputRef.current.value = "";
      fileInputRef.current.click();
    }
  };

  const handleImport = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.currentTarget.files?.[0];
    // 选择后立即复位；失败时也允许再次选择同一个修正后的文件。
    event.currentTarget.value = "";
    if (!file) return;

    setImporting(true);
    setFeedback(null);
    try {
      validateReplayFileSize(file.size);
      const replay = parseReplayText(await file.text());
      const imported = await importReplayDocument(replay);
      await refresh();
      setFeedback({
        type: "success",
        text: `导入成功：${imported.myName || "我方"} vs ${imported.opponentName || "对手"}`,
      });
    } catch (error) {
      setFeedback({
        type: "error",
        text: error instanceof ReplayFileError
          ? error.message
          : "导入回放失败，请确认浏览器允许本地存储后重试。",
      });
    } finally {
      setImporting(false);
    }
  };

  const handleExport = async (meta: MatchMeta) => {
    if (exportingId) return;
    setExportingId(meta.id);
    setFeedback(null);
    try {
      const snapshots = await getSnapshots(meta.id);
      if (!snapshots || snapshots.length === 0) {
        throw new ReplayFileError("无法导出回放：未找到完整快照");
      }
      const replay = createReplayDocument(meta, snapshots);
      const text = serializeReplayDocument(replay);
      const url = URL.createObjectURL(new Blob([text], { type: "application/json;charset=utf-8" }));
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = createReplayFilename(meta);
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      window.setTimeout(() => URL.revokeObjectURL(url), 0);
      setFeedback({ type: "success", text: "回放文件已导出。" });
    } catch (error) {
      setFeedback({
        type: "error",
        text: error instanceof ReplayFileError
          ? error.message
          : "导出回放失败，请稍后重试。",
      });
    } finally {
      setExportingId(null);
    }
  };

  if (source === "cloud") {
    return <CloudReplayPanel onShowLocal={() => setSource("local")} />;
  }

  return (
    <div className="flex h-full flex-col p-3 @[640px]:p-6">
      <div className="mb-3 flex flex-wrap items-start gap-3 @[640px]:mb-4">
        <div className="min-w-0 flex-1">
          <h2 className="text-xl font-bold text-white">对局记录</h2>
          <div className="mt-2 inline-flex rounded-lg border border-gray-700 bg-gray-950 p-1" role="tablist" aria-label="记录来源">
            <button type="button" role="tab" aria-selected="true" className="min-h-11 rounded-md bg-orange-500 px-4 text-sm font-bold text-white">本机</button>
            <button type="button" role="tab" aria-selected="false" onClick={() => setSource("cloud")} className="min-h-11 rounded-md px-4 text-sm text-gray-400">云端</button>
          </div>
          <p className="mt-1 text-xs text-gray-500">仅保存在本设备浏览器，最多保留最近 30 局</p>
        </div>
        <div className="ml-auto flex shrink-0 flex-wrap justify-end gap-2">
          <input
            ref={fileInputRef}
            type="file"
            accept=".json,application/json"
            className="hidden"
            aria-label="选择回放文件"
            onChange={handleImport}
          />
          <button
            type="button"
            onClick={handleChooseImport}
            disabled={importing}
            aria-busy={importing}
            className="min-h-11 rounded-lg border border-orange-500/70 px-3 text-sm text-orange-300 transition-colors hover:border-orange-400 hover:text-orange-200 disabled:cursor-wait disabled:opacity-60 @[640px]:text-xs"
          >
            {importing ? "导入中…" : t("导入")}
          </button>
          {list.length > 0 && (
            <button
              type="button"
              onClick={handleClear}
              className="min-h-11 rounded-lg border border-gray-700 px-3 text-sm text-gray-400 transition-colors hover:border-red-600 hover:text-red-400 @[640px]:text-xs"
            >
              清空
            </button>
          )}
        </div>
      </div>

      <div className="min-h-5" aria-live="polite" aria-atomic="true">
        {feedback && (
          <p
            role={feedback.type === "error" ? "alert" : "status"}
            className={`mb-2 text-xs ${feedback.type === "error" ? "text-red-400" : "text-emerald-400"}`}
          >
            {feedback.text}
          </p>
        )}
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto">
        {loading ? (
          <p className="py-12 text-center text-sm text-gray-600">加载中…</p>
        ) : list.length === 0 ? (
          <p className="py-12 text-center text-sm text-gray-600">
            暂无对局记录，打一局后会出现在这里
          </p>
        ) : (
          <ul className="flex flex-col gap-2">
            {list.map((m) => {
              const openingLabels = getMatchOpeningLabels(m);
              return (
                <li
                  key={m.id}
                  className="group flex items-center gap-2 rounded-xl border border-gray-800 bg-gray-900 px-2 py-2 transition-colors hover:border-orange-600/60 @[640px]:gap-3 @[640px]:px-4 @[640px]:py-3"
                >
                  <button
                    type="button"
                    onClick={() => router.push(`/replay/${encodeURIComponent(m.id)}`)}
                    className="flex min-h-11 min-w-0 flex-1 items-center gap-3 text-left"
                    title="点击观看回放"
                  >
                    <span
                      className={`shrink-0 rounded-md px-2 py-1 text-xs font-bold ${
                        m.isDraw
                          ? "bg-sky-500/20 text-sky-300"
                          : m.winnerIsMe
                          ? "bg-yellow-500/20 text-yellow-400"
                          : "bg-gray-700/60 text-gray-400"
                      }`}
                    >
                      {m.isDraw ? "平" : m.winnerIsMe ? "胜" : "负"}
                    </span>
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-sm text-white">
                        <span className="text-sky-300">{leaderLabel(m.myLeader)}</span>
                        <span className="mx-1.5 text-gray-600">vs</span>
                        <span className="text-red-300">{leaderLabel(m.opponentLeader)}</span>
                      </p>
                      <p className="mt-0.5 truncate text-xs text-gray-500">
                        对手 {m.opponentName || "—"} · {m.turnCount} 回合 · {fmtTime(m.startedAt, locale)}
                      </p>
                      {openingLabels.length > 0 && (
                        <div className="mt-1 flex min-w-0 flex-wrap gap-1" aria-label="开局结果">
                          {openingLabels.map((label) => (
                            <span
                              key={label}
                              className="max-w-full rounded border border-gray-700 bg-gray-800/80 px-1.5 py-0.5 text-[11px] leading-4 text-gray-300"
                            >
                              {label}
                            </span>
                          ))}
                        </div>
                      )}
                    </div>
                    <span className="shrink-0 text-sm text-gray-600 transition-colors group-hover:text-orange-400">
                      ▶ <span className="hidden @[640px]:inline">回放</span>
                    </span>
                  </button>
                  <button
                    type="button"
                    onClick={() => handleExport(m)}
                    disabled={exportingId !== null}
                    className="flex h-11 min-w-11 shrink-0 items-center justify-center rounded-lg px-2 text-sm text-gray-500 transition-colors hover:bg-gray-800 hover:text-orange-300 disabled:cursor-wait disabled:opacity-50 @[640px]:gap-1.5 @[640px]:px-3"
                    title="导出此回放"
                    aria-label={`导出与 ${m.opponentName || "对手"} 的回放`}
                    aria-busy={exportingId === m.id}
                  >
                    <span aria-hidden="true">⇩</span>
                    <span className="hidden @[640px]:inline">
                      {exportingId === m.id ? "导出中…" : t("导出")}
                    </span>
                  </button>
                  <button
                    type="button"
                    onClick={() => handleDelete(m.id)}
                    className="flex h-11 w-11 shrink-0 items-center justify-center rounded-lg text-sm text-gray-600 transition-colors hover:bg-gray-800 hover:text-red-400"
                    title="删除此记录"
                    aria-label={`删除与 ${m.opponentName || "对手"} 的记录`}
                  >
                    ✕
                  </button>
                </li>
              );
            })}
          </ul>
        )}
      </div>
    </div>
  );
}
