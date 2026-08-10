"use client";

import { Fragment, useEffect, useMemo, useState } from "react";
import Image from "next/image";
import { useLanguage } from "@/i18n/LanguageProvider";
import { HomeRequest } from "@/net/HomeProtocol";
import { leaderMatchupKey, useNetStore } from "@/store/netStore";
import { getCard, loadAllCards } from "@/data/CardLoader";
import { advanceImageFallback, CARD_BACK_SRC, thumbSrc } from "@/lib/sprite";
import { LeaderChampionBadge } from "@/components/ui/LeaderChampionBadge";
import {
  nextLeaderLeaderboardSort,
  sortLeaderLeaderboardItems,
  type LeaderLeaderboardSortKey,
  type LeaderLeaderboardSortState,
} from "@/lib/leaderLeaderboardSort";
import type { LeaderboardPeriod, LeaderLeaderboardItem } from "@/types/net";
import LeaderMatchupBreakdown from "./LeaderMatchupBreakdown";
import LeaderMatchupMatrix from "./LeaderMatchupMatrix";

const PERIODS: Array<{ value: LeaderboardPeriod; label: string }> = [
  { value: "7d", label: "近 7 天" },
  { value: "30d", label: "近 30 天" },
  { value: "all", label: "全部" },
];

function percent(value: number | null): string {
  return value == null ? "—" : `${(value * 100).toFixed(1)}%`;
}

function ChampionOwner({
  item,
  compact = false,
}: {
  item: LeaderLeaderboardItem;
  compact?: boolean;
}) {
  if (!item.champion) {
    return <span className="text-xs text-gray-600">最强使用者待诞生</span>;
  }

  return (
    <div className={`flex min-w-0 items-center gap-2 ${compact ? "" : "py-1"}`}>
      <LeaderChampionBadge leaderNumber={item.leaderNumber} />
      <div className="min-w-0">
        <p className="truncate text-xs font-black text-amber-100">{item.champion.displayName}</p>
        <p className="mt-0.5 whitespace-nowrap text-[10px] text-amber-200/65">
          近 30 日 {item.champion.wins}/{item.champion.games} · {percent(item.champion.winRate)}
        </p>
      </div>
    </div>
  );
}

function formatGeneratedAt(value: string | undefined, locale: string): string {
  if (!value) return "";
  try {
    return new Date(value).toLocaleString(locale, {
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
    });
  } catch {
    return "";
  }
}

/** 榜单统一展示当前已收录的最后一张异画，与卡组编辑器的默认异画规则一致。 */
function latestLeaderSprite(card: ReturnType<typeof getCard>): string {
  if (card?.sprites?.length) {
    return card.sprites[card.sprites.length - 1];
  }
  return card?.sprite ?? CARD_BACK_SRC;
}

function colorClasses(color?: string): string {
  if (color?.includes("红")) return "bg-red-500/15 text-red-300 border-red-500/30";
  if (color?.includes("绿")) return "bg-green-500/15 text-green-300 border-green-500/30";
  if (color?.includes("蓝")) return "bg-blue-500/15 text-blue-300 border-blue-500/30";
  if (color?.includes("紫")) return "bg-purple-500/15 text-purple-300 border-purple-500/30";
  if (color?.includes("黑")) return "bg-gray-500/15 text-gray-300 border-gray-500/30";
  if (color?.includes("黄")) return "bg-yellow-500/15 text-yellow-300 border-yellow-500/30";
  return "bg-gray-800 text-gray-400 border-gray-700";
}

const SORT_LABELS: Record<LeaderLeaderboardSortKey, string> = {
  games: "场次",
  record: "战绩",
  winRate: "胜率",
  usageRate: "使用率",
  firstWinRate: "先攻",
  secondWinRate: "后攻",
};

function SortableHeader({
  column,
  sort,
  onSort,
  className = "px-3",
}: {
  column: LeaderLeaderboardSortKey;
  sort: LeaderLeaderboardSortState | null;
  onSort: (column: LeaderLeaderboardSortKey) => void;
  className?: string;
}) {
  const activeDirection = sort?.key === column ? sort.direction : null;
  const nextDirection = activeDirection === "desc" ? "从低到高" : activeDirection === "asc" ? "默认" : "从高到低";
  const label = SORT_LABELS[column];

  return (
    <th
      className={`${className} py-1 text-right`}
      aria-sort={activeDirection === "desc" ? "descending" : activeDirection === "asc" ? "ascending" : "none"}
    >
      <button
        type="button"
        onClick={() => onSort(column)}
        title={`${label}：${activeDirection === "desc" ? "从高到低" : activeDirection === "asc" ? "从低到高" : "默认顺序"}，点击切换为${nextDirection}`}
        className={`flex min-h-10 w-full items-center justify-end gap-1 rounded px-1 font-bold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-orange-500/70 ${
          activeDirection ? "text-orange-300" : "text-gray-500 hover:text-gray-200"
        }`}
      >
        <span>{label}</span>
        <span className={`w-3 text-center text-[10px] ${activeDirection ? "text-orange-400" : "text-gray-700"}`} aria-hidden="true">
          {activeDirection === "desc" ? "▼" : activeDirection === "asc" ? "▲" : "↕"}
        </span>
        <span className="sr-only">
          {activeDirection === "desc" ? "，当前从高到低" : activeDirection === "asc" ? "，当前从低到高" : "，当前默认顺序"}
        </span>
      </button>
    </th>
  );
}

export default function LeaderLeaderboardPanel() {
  const { locale } = useLanguage();
  const leaderboard = useNetStore((s) => s.leaderLeaderboard);
  const leaderMatchups = useNetStore((s) => s.leaderMatchups);
  const leaderMatchupMatrix = useNetStore((s) => s.leaderMatchupMatrix);
  const [period, setPeriod] = useState<LeaderboardPeriod>("7d");
  const [search, setSearch] = useState("");
  const [cardRevision, setCardRevision] = useState(0);
  const [selectedLeader, setSelectedLeader] = useState<string | null>(null);
  const [viewMode, setViewMode] = useState<"ranking" | "matrix">("ranking");
  const [sort, setSort] = useState<LeaderLeaderboardSortState | null>(null);

  const request = (nextPeriod: LeaderboardPeriod) => {
    setSelectedLeader(null);
    HomeRequest.requestLeaderLeaderboard(nextPeriod);
  };

  const toggleMatchups = (leaderNumber: string) => {
    if (selectedLeader === leaderNumber) {
      setSelectedLeader(null);
      return;
    }
    setSelectedLeader(leaderNumber);
    HomeRequest.requestLeaderMatchups(period, leaderNumber);
  };

  useEffect(() => {
    let cancelled = false;
    loadAllCards()
      .then(() => {
        if (!cancelled) setCardRevision((value) => value + 1);
      })
      .catch(() => {
        // 卡牌总包不可用时仍展示卡号和统计，不阻断榜单。
      });
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    setSelectedLeader(null);
    HomeRequest.requestLeaderLeaderboard(period);
  }, [period]);

  useEffect(() => {
    if (
      viewMode === "matrix"
      && leaderboard?.period === period
      && leaderboard.result !== false
      && (leaderMatchupMatrix == null || leaderMatchupMatrix.period !== period)
    ) {
      HomeRequest.requestLeaderMatchupMatrix(period);
    }
  }, [leaderboard, leaderMatchupMatrix, period, viewMode]);

  const items = useMemo(() => {
    const keyword = search.trim().toLocaleLowerCase("zh-CN");
    const source = leaderboard?.period === period ? leaderboard.items ?? [] : [];
    const filtered = !keyword ? source : source.filter((item) => {
      const card = getCard(item.leaderNumber);
      return item.leaderNumber.toLocaleLowerCase("zh-CN").includes(keyword)
        || card?.name.toLocaleLowerCase("zh-CN").includes(keyword);
    });
    return sortLeaderLeaderboardItems(filtered, sort);
  }, [cardRevision, leaderboard, period, search, sort]);

  const loading = leaderboard == null || leaderboard.period !== period;
  const failed = !loading && leaderboard.result === false;
  const selectedMatchups = selectedLeader
    ? leaderMatchups[leaderMatchupKey(period, selectedLeader)]
    : undefined;

  return (
    <section className="flex h-full min-h-0 flex-col p-3 @[640px]:p-6">
      <header className="mb-4 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-xl font-bold text-white">Leader 胜率榜</h2>
          <p className="mt-1 text-sm leading-5 text-gray-500 @[640px]:text-xs">
            统计全部真人对局；第 7 回合及以前或因掉线结束的对局不计入数据 · 支持排行榜与对阵一图流
          </p>
        </div>
        <div className="grid w-full grid-cols-[1fr_auto] items-center gap-2 @[640px]:flex @[640px]:w-auto">
          <div className="grid grid-cols-3 rounded-lg border border-gray-800 bg-gray-950 p-1">
            {PERIODS.map((option) => (
              <button
                key={option.value}
                type="button"
                onClick={() => setPeriod(option.value)}
                className={`min-h-11 rounded-md px-2 text-xs font-bold transition-colors @[640px]:min-h-0 @[640px]:px-3 @[640px]:py-1.5 ${
                  period === option.value
                    ? "bg-orange-500 text-white"
                    : "text-gray-500 hover:bg-gray-800 hover:text-gray-200"
                }`}
              >
                {option.label}
              </button>
            ))}
          </div>
          <button
            type="button"
            onClick={() => request(period)}
            className="min-h-11 rounded-lg border border-gray-800 bg-gray-950 px-3 text-sm text-gray-400 transition-colors hover:border-orange-500 hover:text-white @[640px]:min-h-0 @[640px]:py-2 @[640px]:text-xs"
          >
            刷新
          </button>
        </div>
      </header>

      <div className="mb-3 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-gray-800 bg-gray-900/70 px-3 py-3 @[640px]:px-4">
        <div className="flex flex-wrap items-center gap-x-5 gap-y-2 text-sm text-gray-400 @[640px]:text-xs">
          <span>
            有效对局 <strong className="ml-1 text-white">{leaderboard?.totalMatches ?? 0}</strong>
          </span>
          <span>
            排名门槛 <strong className="ml-1 text-white">{leaderboard?.minimumGames ?? 20} 场</strong>
          </span>
          {leaderboard?.generatedAtUtc && (
            <span className="hidden text-gray-600 @[640px]:inline">
              更新于 {formatGeneratedAt(leaderboard.generatedAtUtc, locale)}
            </span>
          )}
        </div>
        <div className="flex w-full flex-wrap items-center gap-2 @[640px]:w-auto">
          <div className="grid flex-1 grid-cols-2 rounded-lg border border-gray-800 bg-gray-950 p-1 @[640px]:flex-none">
            <button
              type="button"
              onClick={() => setViewMode("ranking")}
              className={`min-h-9 rounded-md px-3 text-xs font-bold transition-colors ${
                viewMode === "ranking" ? "bg-gray-700 text-white" : "text-gray-500 hover:text-gray-200"
              }`}
            >
              排行榜
            </button>
            <button
              type="button"
              onClick={() => setViewMode("matrix")}
              className={`min-h-9 rounded-md px-3 text-xs font-bold transition-colors ${
                viewMode === "matrix" ? "bg-orange-500 text-white" : "text-gray-500 hover:text-gray-200"
              }`}
            >
              对阵一图流
            </button>
          </div>
          {viewMode === "ranking" && (
            <input
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="搜索 Leader 名称或卡号"
              className="h-11 w-full rounded-lg border border-gray-700 bg-gray-950 px-3 text-base text-white outline-none placeholder:text-gray-600 focus:border-orange-500 @[640px]:h-auto @[640px]:w-56 @[640px]:py-1.5 @[640px]:text-xs"
            />
          )}
        </div>
      </div>

      <div className="min-h-0 flex-1 overflow-auto rounded-xl border border-gray-800 bg-gray-950/60">
        {loading ? (
          <p className="py-16 text-center text-sm text-gray-600">正在加载排行榜…</p>
        ) : failed ? (
          <div className="py-16 text-center">
            <p className="text-sm text-red-400">{leaderboard.error ?? "排行榜暂时不可用"}</p>
            <button
              type="button"
              onClick={() => request(period)}
              className="mt-3 rounded-lg border border-gray-700 px-3 py-1.5 text-xs text-gray-300 hover:border-orange-500"
            >
              重试
            </button>
          </div>
        ) : viewMode === "matrix" ? (
          <LeaderMatchupMatrix
            data={leaderMatchupMatrix?.period === period ? leaderMatchupMatrix : null}
            leaderboardItems={leaderboard.items ?? []}
            onRetry={() => HomeRequest.requestLeaderMatchupMatrix(period)}
          />
        ) : items.length === 0 ? (
          <p className="py-16 text-center text-sm text-gray-600">
            {search ? "没有符合搜索条件的 Leader" : "当前时间范围暂无有效对局"}
          </p>
        ) : (
          <>
          <ul className="divide-y divide-gray-800/80 @[1024px]:hidden">
            {items.map((item) => {
              const card = getCard(item.leaderNumber);
              const expanded = selectedLeader === item.leaderNumber;
              return (
                <li key={item.leaderNumber} className={`p-3 transition-colors ${expanded ? "bg-orange-500/[0.04]" : ""}`}>
                  <div
                    role="button"
                    tabIndex={0}
                    aria-expanded={expanded}
                    onClick={() => toggleMatchups(item.leaderNumber)}
                    onKeyDown={(event) => {
                      if (event.key === "Enter" || event.key === " ") {
                        event.preventDefault();
                        toggleMatchups(item.leaderNumber);
                      }
                    }}
                    className="flex cursor-pointer items-center gap-3 rounded-lg outline-none focus-visible:ring-2 focus-visible:ring-orange-500/70"
                  >
                    <span className={`w-7 shrink-0 text-center text-lg font-black ${item.rank != null && item.rank <= 3 ? "text-orange-400" : "text-gray-400"}`}>
                      {item.rank ?? "—"}
                    </span>
                    <div className="relative h-[62px] w-11 shrink-0 overflow-hidden rounded-md border border-gray-700 bg-gray-900">
                      <Image
                        src={thumbSrc(latestLeaderSprite(card))}
                        alt={card?.name ?? item.leaderNumber}
                        fill
                        sizes="44px"
                        className="object-cover"
                        onError={(event) => advanceImageFallback(event.currentTarget, [latestLeaderSprite(card), card?.image])}
                      />
                    </div>
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-sm font-bold text-white">{card?.name ?? item.leaderNumber}</p>
                      <div className="mt-1 flex flex-wrap items-center gap-1.5">
                        <span className="text-xs text-gray-500">{item.leaderNumber}</span>
                        {card?.color && <span className={`rounded border px-1.5 py-0.5 text-xs ${colorClasses(card.color)}`}>{card.color}</span>}
                        {item.insufficientSample && <span className="rounded bg-gray-800 px-1.5 py-0.5 text-xs text-gray-500">样本不足</span>}
                      </div>
                      <div className="mt-2">
                        <ChampionOwner item={item} compact />
                      </div>
                    </div>
                    <div className="shrink-0 text-right">
                      <p className="text-lg font-black text-orange-300">{percent(item.winRate)}</p>
                      <p className="text-xs text-gray-600">胜率 <span className="ml-1">{expanded ? "▴" : "▾"}</span></p>
                    </div>
                  </div>
                  <div className="mt-3 grid grid-cols-3 gap-2 rounded-xl bg-gray-900 px-3 py-2.5 text-center">
                    <div><p className="text-sm font-bold text-gray-200">{item.games}</p><p className="text-xs text-gray-600">场次</p></div>
                    <div><p className="text-sm"><span className="text-emerald-400">{item.wins}</span><span className="mx-1 text-gray-700">-</span><span className="text-red-400">{item.losses}</span></p><p className="text-xs text-gray-600">战绩</p></div>
                    <div><p className="text-sm font-bold text-gray-200">{percent(item.usageRate)}</p><p className="text-xs text-gray-600">使用率</p></div>
                  </div>
                  <div className="mt-2 flex justify-between px-1 text-xs text-gray-500">
                    <span>先攻 {percent(item.firstWinRate)} · {item.firstGames} 场</span>
                    <span>后攻 {percent(item.secondWinRate)} · {item.secondGames} 场</span>
                  </div>
                  {expanded && (
                    <div className="mt-3">
                      <LeaderMatchupBreakdown
                        data={selectedMatchups}
                        onRetry={() => HomeRequest.requestLeaderMatchups(period, item.leaderNumber)}
                      />
                    </div>
                  )}
                </li>
              );
            })}
          </ul>
          <table className="hidden w-full min-w-[1120px] border-collapse text-left @[1024px]:table">
            <thead className="sticky top-0 z-10 bg-gray-900 text-[11px] uppercase tracking-wide text-gray-500">
              <tr>
                <th className="w-16 px-4 py-3 text-center">排名</th>
                <th className="px-3 py-3">Leader</th>
                <th className="w-56 px-3 py-3">最强使用者</th>
                {(["games", "record", "winRate", "usageRate", "firstWinRate", "secondWinRate"] as const).map((column) => (
                  <SortableHeader
                    key={column}
                    column={column}
                    sort={sort}
                    onSort={(nextColumn) => setSort((current) => nextLeaderLeaderboardSort(current, nextColumn))}
                    className={column === "secondWinRate" ? "px-4" : "px-3"}
                  />
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-800/80">
              {items.map((item) => {
                const card = getCard(item.leaderNumber);
                const expanded = selectedLeader === item.leaderNumber;
                return (
                  <Fragment key={item.leaderNumber}>
                  <tr
                    role="button"
                    tabIndex={0}
                    aria-expanded={expanded}
                    onClick={() => toggleMatchups(item.leaderNumber)}
                    onKeyDown={(event) => {
                      if (event.key === "Enter" || event.key === " ") {
                        event.preventDefault();
                        toggleMatchups(item.leaderNumber);
                      }
                    }}
                    className={`cursor-pointer outline-none transition-colors hover:bg-gray-900/80 focus-visible:bg-gray-900 ${expanded ? "bg-orange-500/[0.04]" : ""}`}
                  >
                    <td className="px-4 py-2.5 text-center">
                      {item.rank != null ? (
                        <span className={`font-black ${item.rank <= 3 ? "text-orange-400" : "text-gray-300"}`}>
                          {item.rank}
                        </span>
                      ) : (
                        <span className="text-xs text-gray-700">—</span>
                      )}
                    </td>
                    <td className="px-3 py-2.5">
                      <div className="flex items-center gap-3">
                        <div className="relative h-[62px] w-11 shrink-0 overflow-hidden rounded-md border border-gray-700 bg-gray-900">
                          <Image
                            src={thumbSrc(latestLeaderSprite(card))}
                            alt={card?.name ?? item.leaderNumber}
                            fill
                            sizes="44px"
                            className="object-cover"
                            onError={(event) => advanceImageFallback(event.currentTarget, [latestLeaderSprite(card), card?.image])}
                          />
                        </div>
                        <div className="min-w-0">
                          <p className="truncate text-sm font-bold text-white">
                            {card?.name ?? item.leaderNumber}
                          </p>
                          <div className="mt-1 flex items-center gap-1.5">
                            <span className="text-[11px] text-gray-500">{item.leaderNumber}</span>
                            {card?.color && (
                              <span className={`rounded border px-1.5 py-0.5 text-[10px] ${colorClasses(card.color)}`}>
                                {card.color}
                              </span>
                            )}
                            {item.insufficientSample && (
                              <span className="rounded bg-gray-800 px-1.5 py-0.5 text-[10px] text-gray-500">
                                样本不足
                              </span>
                            )}
                          </div>
                        </div>
                        <span className={`ml-auto text-xs text-gray-600 transition-transform ${expanded ? "rotate-180" : ""}`} aria-hidden="true">▾</span>
                      </div>
                    </td>
                    <td className="px-3 py-2.5">
                      <ChampionOwner item={item} />
                    </td>
                    <td className="px-3 py-2.5 text-right text-sm font-semibold text-gray-200">{item.games}</td>
                    <td className="px-3 py-2.5 text-right text-sm">
                      <span className="text-emerald-400">{item.wins}</span>
                      <span className="mx-1 text-gray-700">-</span>
                      <span className="text-red-400">{item.losses}</span>
                    </td>
                    <td className="px-3 py-2.5 text-right text-sm font-black text-orange-300">
                      {percent(item.winRate)}
                    </td>
                    <td className="px-3 py-2.5 text-right text-sm text-gray-300">{percent(item.usageRate)}</td>
                    <td className="px-3 py-2.5 text-right">
                      <p className="text-sm text-gray-200">{percent(item.firstWinRate)}</p>
                      <p className="text-[10px] text-gray-600">{item.firstGames} 场</p>
                    </td>
                    <td className="px-4 py-2.5 text-right">
                      <p className="text-sm text-gray-200">{percent(item.secondWinRate)}</p>
                      <p className="text-[10px] text-gray-600">{item.secondGames} 场</p>
                    </td>
                  </tr>
                  {expanded && (
                    <tr className="bg-gray-950/80">
                      <td colSpan={9} className="px-3 py-3">
                        <LeaderMatchupBreakdown
                          data={selectedMatchups}
                          onRetry={() => HomeRequest.requestLeaderMatchups(period, item.leaderNumber)}
                        />
                      </td>
                    </tr>
                  )}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
          </>
        )}
      </div>
    </section>
  );
}
