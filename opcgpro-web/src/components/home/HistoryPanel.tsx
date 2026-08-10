"use client";

import { useEffect, useState, useCallback } from "react";
import { useRouter } from "next/navigation";
import { listMeta, deleteMatch, clearAll, type MatchMeta } from "@/data/matchHistoryDB";
import { getCard } from "@/data/CardLoader";
import { useLanguage } from "@/i18n/LanguageProvider";

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

  return (
    <div className="flex h-full flex-col p-3 @[640px]:p-6">
      <div className="mb-4 flex items-center justify-between">
        <div>
          <h2 className="text-xl font-bold text-white">对局记录</h2>
          <p className="mt-0.5 text-xs text-gray-500">
            仅保存在本设备浏览器，最多保留最近 30 局
          </p>
        </div>
        {list.length > 0 && (
          <button
            onClick={handleClear}
            className="min-h-11 rounded-lg border border-gray-700 px-3 text-sm text-gray-400 transition-colors hover:border-red-600 hover:text-red-400 @[640px]:min-h-0 @[640px]:py-1.5 @[640px]:text-xs"
          >
            清空
          </button>
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
            {list.map((m) => (
              <li
                key={m.id}
                className="group flex items-center gap-2 rounded-xl border border-gray-800 bg-gray-900 px-2 py-2 transition-colors hover:border-orange-600/60 @[640px]:gap-3 @[640px]:px-4 @[640px]:py-3"
              >
                <button
                  onClick={() => router.push(`/replay/${encodeURIComponent(m.id)}`)}
                  className="flex min-w-0 flex-1 items-center gap-3 text-left"
                  title="点击观看回放"
                >
                  <span
                    className={`shrink-0 rounded-md px-2 py-1 text-xs font-bold ${
                      m.winnerIsMe
                        ? "bg-yellow-500/20 text-yellow-400"
                        : "bg-gray-700/60 text-gray-400"
                    }`}
                  >
                    {m.winnerIsMe ? "胜" : "负"}
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
                  </div>
                  <span className="shrink-0 text-sm text-gray-600 transition-colors group-hover:text-orange-400">
                    ▶ <span className="hidden @[640px]:inline">回放</span>
                  </span>
                </button>
                <button
                  onClick={() => handleDelete(m.id)}
                  className="flex h-11 w-11 shrink-0 items-center justify-center rounded-lg text-sm text-gray-600 transition-colors hover:bg-gray-800 hover:text-red-400"
                  title="删除此记录"
                >
                  ✕
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}
