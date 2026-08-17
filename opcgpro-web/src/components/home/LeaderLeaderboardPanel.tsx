"use client";

import { Fragment, useEffect, useMemo, useState } from "react";
import Image from "next/image";
import { useLanguage } from "@/i18n/LanguageProvider";
import { HomeRequest } from "@/net/HomeProtocol";
import { leaderMatchupKey, useNetStore } from "@/store/netStore";
import { getCard, loadAllCards } from "@/data/CardLoader";
import { advanceImageFallback, CARD_BACK_SRC, thumbSrc } from "@/lib/sprite";
import { formatRankBounty } from "@/lib/rankBounty";
import { LeaderChampionBadge, LeaderChampionBadgeList } from "@/components/ui/LeaderChampionBadge";
import RankTierBadge from "@/components/ui/RankTierBadge";
import Modal from "@/components/ui/Modal";
import {
  nextLeaderLeaderboardSort,
  sortLeaderLeaderboardItems,
  type LeaderLeaderboardSortKey,
  type LeaderLeaderboardSortState,
} from "@/lib/leaderLeaderboardSort";
import type { FactionStanding, LeaderboardPeriod, LeaderLeaderboardItem, RankFaction, RankedMode, RankLeaderboardItem } from "@/types/net";
import LeaderMatchupBreakdown from "./LeaderMatchupBreakdown";
import LeaderMatchupMatrix from "./LeaderMatchupMatrix";

const PERIODS: Array<{ value: LeaderboardPeriod; label: string }> = [
  { value: "7d", label: "近 7 天" },
  { value: "30d", label: "近 30 天" },
  { value: "all", label: "全部" },
];

const RANK_FACTION_NAMES: Record<RankFaction, string> = {
  pirate: "海贼",
  marine: "海军",
  government: "世界政府",
};

function RankedMobileRow({ item, pinned = false }: { item: RankLeaderboardItem; pinned?: boolean }) {
  return (
    <li className={`p-3 ${pinned ? "bg-violet-500/[0.12] ring-1 ring-inset ring-violet-400/40" : ""}`}>
      {pinned && <p className="mb-2 text-[11px] font-black tracking-[0.18em] text-violet-300">我的排名</p>}
      <div className="flex items-center gap-3">
        <span className={`w-9 shrink-0 text-center text-lg font-black ${item.rank <= 3 ? "text-violet-300" : "text-gray-300"}`}>#{item.rank}</span>
        <div className="min-w-0 flex-1">
          <div className="flex min-w-0 items-center gap-1.5">
            <p className="truncate text-sm font-bold text-white">{item.displayName}</p>
            <LeaderChampionBadgeList leaderNumbers={item.championLeaderNumbers} maxVisible={1} />
          </div>
          <div className="mt-1 flex min-w-0 flex-wrap items-center gap-x-1.5 gap-y-1 text-xs text-gray-500">
            <span>{RANK_FACTION_NAMES[item.faction]}</span>
            <span aria-hidden="true">·</span>
            <RankTierBadge faction={item.faction} tier={item.tier} division={item.division} />
          </div>
          <p className="mt-1 truncate text-xs text-amber-200/80">擅长 {item.favoriteLeader ? getCard(item.favoriteLeader)?.name ?? item.favoriteLeader : "暂无统计"}</p>
        </div>
        <div className="shrink-0 text-right">
          <p className="max-w-28 text-sm font-black leading-5 text-violet-200">{formatRankBounty(item.rankPoints)}</p>
          <p className="text-[11px] text-gray-600">悬赏金</p>
        </div>
      </div>
      <div className="mt-3 grid grid-cols-3 gap-2 rounded-xl bg-gray-900/90 px-3 py-2.5 text-center text-xs">
        <div><p className="font-bold text-gray-200">{item.games}</p><p className="mt-1 text-gray-600">场次</p></div>
        <div><p><span className="text-emerald-400">{item.wins}</span><span className="mx-1 text-gray-700">-</span><span className="text-red-400">{item.games - item.wins}</span></p><p className="mt-1 text-gray-600">战绩</p></div>
        <div><p className="font-bold text-violet-200">{item.winRate.toFixed(1)}%</p><p className="mt-1 text-gray-600">胜率</p></div>
      </div>
    </li>
  );
}

function RankedDesktopRow({ item, pinned = false }: { item: RankLeaderboardItem; pinned?: boolean }) {
  return (
    <tr className={`transition-colors ${pinned ? "bg-violet-500/[0.12] ring-1 ring-inset ring-violet-400/40" : "hover:bg-gray-900/80"}`}>
      <td className={`w-20 px-4 py-3 text-center font-black ${item.rank <= 3 ? "text-violet-300" : "text-gray-300"}`}>#{item.rank}</td>
      <td className="px-3 py-3">
        <div className="flex min-w-0 items-center gap-2">
          <span className="truncate text-sm font-bold text-white">{item.displayName}</span>
          {pinned && <span className="shrink-0 rounded bg-violet-500/20 px-1.5 py-0.5 text-[10px] font-black text-violet-200">我</span>}
          <LeaderChampionBadgeList leaderNumbers={item.championLeaderNumbers} />
        </div>
      </td>
      <td className="px-3 py-3 text-sm text-gray-300">{RANK_FACTION_NAMES[item.faction]}</td>
      <td className="px-3 py-3 text-sm text-gray-300"><RankTierBadge faction={item.faction} tier={item.tier} division={item.division} /></td>
      <td className="px-3 py-3 text-sm text-amber-200/80">{item.favoriteLeader ? getCard(item.favoriteLeader)?.name ?? item.favoriteLeader : "暂无统计"}</td>
      <td className="whitespace-nowrap px-3 py-3 text-right text-sm font-black text-violet-200">{formatRankBounty(item.rankPoints)}</td>
      <td className="px-3 py-3 text-right text-sm text-gray-200">{item.games}</td>
      <td className="px-3 py-3 text-right text-sm"><span className="text-emerald-400">{item.wins}</span><span className="mx-1 text-gray-700">-</span><span className="text-red-400">{item.games - item.wins}</span></td>
      <td className="px-4 py-3 text-right text-sm font-bold text-violet-200">{item.winRate.toFixed(1)}%</td>
    </tr>
  );
}

function RankedTable({ items, pinned = false }: { items: RankLeaderboardItem[]; pinned?: boolean }) {
  return (
    <table className="hidden w-full min-w-[720px] table-fixed border-collapse text-left @[1024px]:table">
      {!pinned && <thead className="sticky top-0 z-10 bg-gray-900 text-[11px] uppercase tracking-wide text-gray-500">
        <tr>
          <th className="w-20 px-4 py-3 text-center">排名</th>
          <th className="w-[22%] px-3 py-3">昵称</th>
          <th className="w-[10%] px-3 py-3">阵营</th>
          <th className="w-[13%] px-3 py-3">段位</th>
          <th className="w-[18%] px-3 py-3">最擅长 Leader</th>
          <th className="w-[14%] px-3 py-3 text-right">悬赏金</th>
          <th className="w-[7%] px-3 py-3 text-right">场次</th>
          <th className="w-[9%] px-3 py-3 text-right">战绩</th>
          <th className="w-[8%] px-4 py-3 text-right">胜率</th>
        </tr>
      </thead>}
      <tbody className="divide-y divide-gray-800/80">
        {items.map((item) => <RankedDesktopRow key={`${item.rank}-${item.displayName}-${pinned ? "mine" : "rank"}`} item={item} pinned={pinned} />)}
      </tbody>
    </table>
  );
}

function RankedLeaderboard({ items, standings }: { items: RankLeaderboardItem[]; standings: FactionStanding[] }) {
  const [selectedFaction, setSelectedFaction] = useState<RankFaction | null>(null);
  const topItems = selectedFaction
    ? items
        .filter((item) => item.faction === selectedFaction && item.factionRank <= 100)
        .sort((a, b) => a.factionRank - b.factionRank)
        .map((item) => ({ ...item, rank: item.factionRank }))
    : items.filter((item) => item.rank <= 100);
  const currentPlayerSource = items.find((item) => item.isCurrentPlayer);
  const currentPlayer = currentPlayerSource
    && (!selectedFaction || currentPlayerSource.faction === selectedFaction)
    ? { ...currentPlayerSource, rank: selectedFaction ? currentPlayerSource.factionRank : currentPlayerSource.rank }
    : undefined;

  if (topItems.length === 0 && !currentPlayer) {
    return <p className="py-16 text-center text-sm text-gray-600">本赛季暂时还没有完成定级的玩家。</p>;
  }

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="shrink-0 border-b border-gray-800 bg-gray-950/90 p-2.5">
        <div className="grid grid-cols-2 gap-2 @[720px]:grid-cols-4">
          <button
            type="button"
            onClick={() => setSelectedFaction(null)}
            className={`min-h-11 rounded-lg border px-3 py-2 text-left text-xs ${selectedFaction === null ? "border-violet-400 bg-violet-500/15 text-white" : "border-gray-800 bg-gray-900 text-gray-400"}`}
          >
            <strong className="block">全服个人榜</strong>
            <span className="mt-1 block text-[10px] text-gray-500">按全服排名查看</span>
          </button>
          {standings.map((standing) => (
            <button
              key={standing.faction}
              type="button"
              onClick={() => setSelectedFaction(standing.faction)}
              className={`min-h-11 rounded-lg border px-3 py-2 text-left text-xs ${selectedFaction === standing.faction ? "border-violet-400 bg-violet-500/15 text-white" : "border-gray-800 bg-gray-900 text-gray-400"}`}
            >
              <span className="flex items-center justify-between gap-2"><strong>{RANK_FACTION_NAMES[standing.faction]}</strong><b className="text-amber-300">#{standing.rank}</b></span>
              <span className="mt-1 block text-[10px]">总分 {standing.totalRankPoints.toLocaleString()} · {standing.playerCount} 人</span>
            </button>
          ))}
        </div>
        <p className="mt-2 text-[10px] text-gray-600">阵营总分为本赛季已完成定级成员悬赏金之和；点击阵营可查看内部排行榜。</p>
      </div>
      <div className="min-h-0 flex-1 overflow-auto">
        <ul className="divide-y divide-gray-800/80 @[1024px]:hidden">
          {topItems.map((item) => <RankedMobileRow key={`${item.rank}-${item.displayName}`} item={item} />)}
        </ul>
        <RankedTable items={topItems} />
      </div>
      {currentPlayer && (
        <div className="shrink-0 border-t-2 border-violet-400/50 bg-gray-950 shadow-[0_-12px_28px_rgba(0,0,0,0.45)]">
          <ul className="@[1024px]:hidden"><RankedMobileRow item={currentPlayer} pinned /></ul>
          <RankedTable items={[currentPlayer]} pinned />
        </div>
      )}
    </div>
  );
}

function percent(value: number | null): string {
  return value == null ? "—" : `${(value * 100).toFixed(1)}%`;
}

function ChampionRulesModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  return (
    <Modal open={open} onClose={onClose} title="“最强”称号规则" mobileSheet maxWidthClass="max-w-lg">
      <div className="max-h-[min(68dvh,34rem)] space-y-4 overflow-y-auto pr-1 text-sm leading-6 text-gray-300">
        <p>
          每个 Leader 都会单独评选一名全服最强使用者。评选固定采用<strong className="text-amber-200">近 30 日</strong>数据，
          不会随榜单当前选择的“近 7 天 / 近 30 天 / 全部”切换。
        </p>
        <ol className="list-decimal space-y-3 pl-5 marker:font-bold marker:text-orange-400">
          <li>
            玩家使用该 Leader 完成至少 <strong className="text-white">20 场</strong>有效公开对局后，才会进入候选名单。
          </li>
          <li>
            只统计排位、休闲匹配和普通公开匹配；好友房、房间码及机器人对局不计入。
          </li>
          <li>
            少于 8 回合、掉线结束、没有明确胜负或同账号之间的对局不计入。
          </li>
          <li>
            候选人按 <strong className="text-white">90% Wilson 胜率下限</strong>排名。它会同时考虑胜率和样本量，
            避免少量对局的高胜率轻易超过长期稳定战绩。
          </li>
          <li>
            得分相同时依次比较场次、胜场；仍相同时由服务器的稳定顺序选出唯一持有者。
          </li>
        </ol>
        <p className="rounded-lg border border-amber-500/20 bg-amber-500/10 px-3 py-2 text-xs leading-5 text-amber-100/80">
          称号会随最近 30 日战绩滚动更新，因此持有者可能发生变化。
        </p>
      </div>
    </Modal>
  );
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
  const rankProfiles = useNetStore((s) => s.rankProfiles);
  const rankLeaderboards = useNetStore((s) => s.rankLeaderboards);
  const factionStandingsByMode = useNetStore((s) => s.factionStandingsByMode);
  const leaderMatchups = useNetStore((s) => s.leaderMatchups);
  const leaderMatchupMatrix = useNetStore((s) => s.leaderMatchupMatrix);
  const [period, setPeriod] = useState<LeaderboardPeriod>("7d");
  const [search, setSearch] = useState("");
  const [cardRevision, setCardRevision] = useState(0);
  const [selectedLeader, setSelectedLeader] = useState<string | null>(null);
  const [rankingTab, setRankingTab] = useState<"leader" | "ranked">("ranked");
  const [rankedMode, setRankedMode] = useState<RankedMode>("standard");
  const [viewMode, setViewMode] = useState<"ranking" | "matrix">("ranking");
  const [sort, setSort] = useState<LeaderLeaderboardSortState | null>(null);
  const [championRulesOpen, setChampionRulesOpen] = useState(false);
  const rankProfile = rankProfiles[rankedMode];
  const rankLeaderboard = rankLeaderboards[rankedMode];
  const factionStandings = factionStandingsByMode[rankedMode];

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
    if (rankingTab !== "leader") return;
    setSelectedLeader(null);
    HomeRequest.requestLeaderLeaderboard(period);
  }, [period, rankingTab]);

  useEffect(() => {
    if (rankingTab === "ranked" && !rankProfile) HomeRequest.requestRankSnapshot(rankedMode);
  }, [rankProfile, rankedMode, rankingTab]);

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
          <h2 className="text-xl font-bold text-white">排行榜</h2>
          <p className="mt-1 text-sm leading-5 text-gray-500 @[640px]:text-xs">
            {rankingTab === "leader"
              ? "统计全部真人对局；第 7 回合及以前或因掉线结束的对局不计入数据 · 支持排行榜与对阵一图流"
              : "展示本赛季已完成定级的玩家排名，按悬赏金与隐藏实力排序。"}
          </p>
        </div>
        <div className="flex w-full flex-col gap-2 @[640px]:w-auto @[640px]:items-end">
          <div className="grid w-full grid-cols-2 rounded-lg border border-gray-800 bg-gray-950 p-1 @[640px]:w-auto">
            <button
              type="button"
              onClick={() => setRankingTab("ranked")}
              aria-pressed={rankingTab === "ranked"}
              className={`min-h-11 rounded-md px-3 text-xs font-bold transition-colors @[640px]:min-h-0 @[640px]:py-1.5 ${rankingTab === "ranked" ? "bg-violet-600 text-white" : "text-gray-500 hover:bg-gray-800 hover:text-gray-200"}`}
            >
              排位榜
            </button>
            <button
              type="button"
              onClick={() => setRankingTab("leader")}
              aria-pressed={rankingTab === "leader"}
              className={`min-h-11 rounded-md px-3 text-xs font-bold transition-colors @[640px]:min-h-0 @[640px]:py-1.5 ${rankingTab === "leader" ? "bg-orange-500 text-white" : "text-gray-500 hover:bg-gray-800 hover:text-gray-200"}`}
            >
              Leader榜
            </button>
          </div>
          {rankingTab === "ranked" && (
            <div className="grid w-full grid-cols-2 rounded-lg border border-violet-900/70 bg-gray-950 p-1 @[640px]:w-auto" aria-label="排位榜模式">
              <button
                type="button"
                onClick={() => setRankedMode("standard")}
                aria-pressed={rankedMode === "standard"}
                className={`min-h-11 rounded-md px-4 text-xs font-bold transition-colors @[640px]:min-h-0 @[640px]:py-1.5 ${rankedMode === "standard" ? "bg-violet-600 text-white" : "text-gray-500 hover:bg-gray-800 hover:text-gray-200"}`}
              >
                标准
              </button>
              <button
                type="button"
                onClick={() => setRankedMode("wild")}
                aria-pressed={rankedMode === "wild"}
                className={`min-h-11 rounded-md px-4 text-xs font-bold transition-colors @[640px]:min-h-0 @[640px]:py-1.5 ${rankedMode === "wild" ? "bg-fuchsia-600 text-white" : "text-gray-500 hover:bg-gray-800 hover:text-gray-200"}`}
              >
                狂野
              </button>
            </div>
          )}
          {rankingTab === "leader" && <div className="grid grid-cols-[1fr_auto] items-center gap-2 @[640px]:flex">
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
          </div>}
        </div>
      </header>

      <div className="mb-3 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-gray-800 bg-gray-900/70 px-3 py-3 @[640px]:px-4">
        {rankingTab === "leader" ? <div className="flex flex-wrap items-center gap-x-5 gap-y-2 text-sm text-gray-400 @[640px]:text-xs">
          <span>
            有效对局 <strong className="ml-1 text-white">{leaderboard?.totalMatches ?? 0}</strong>
          </span>
          <span>
            排名门槛 <strong className="ml-1 text-white">{leaderboard?.minimumGames ?? 20} 场</strong>
          </span>
          <span className="flex items-center gap-1.5">
            最强称号
            <button
              type="button"
              onClick={() => setChampionRulesOpen(true)}
              aria-label="查看最强称号规则"
              title="查看最强称号规则"
              className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full border border-amber-500/30 bg-amber-500/10 text-base transition-colors hover:border-amber-400 hover:bg-amber-500/20 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber-400 @[640px]:h-11 @[640px]:w-11"
            >
              <span aria-hidden="true">❓️</span>
            </button>
          </span>
          {leaderboard?.generatedAtUtc && (
            <span className="hidden text-gray-600 @[640px]:inline">
              更新于 {formatGeneratedAt(leaderboard.generatedAtUtc, locale)}
            </span>
          )}
        </div> : <div className="flex flex-wrap items-center gap-x-5 gap-y-2 text-sm text-gray-400 @[640px]:text-xs">
          <span>赛季 <strong className="ml-1 text-white">{rankProfile?.seasonId ?? "加载中"}</strong></span>
          <span>已上榜 <strong className="ml-1 text-white">{rankLeaderboard.length}</strong> 名玩家</span>
          <span className="text-gray-600">{rankedMode === "standard" ? "标准排位" : "狂野排位"}完成定级后进入排行榜</span>
        </div>}
        {rankingTab === "leader" && <div className="flex w-full flex-wrap items-center gap-2 @[640px]:w-auto">
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
        </div>}
      </div>

      <div className={`min-h-0 flex-1 rounded-xl border border-gray-800 bg-gray-950/60 ${rankingTab === "ranked" ? "overflow-hidden" : "overflow-auto"}`}>
        {rankingTab === "ranked" ? rankProfile ? <RankedLeaderboard items={rankLeaderboard} standings={factionStandings} /> : (
          <p className="py-16 text-center text-sm text-gray-600">正在加载{rankedMode === "standard" ? "标准" : "狂野"}排位榜…</p>
        ) : loading ? (
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
      <ChampionRulesModal open={championRulesOpen} onClose={() => setChampionRulesOpen(false)} />
    </section>
  );
}
