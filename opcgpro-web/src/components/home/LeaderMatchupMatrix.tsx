"use client";

import Image from "next/image";
import { getCard } from "@/data/CardLoader";
import { advanceImageFallback, CARD_BACK_SRC, thumbSrc } from "@/lib/sprite";
import {
  LEADER_MATCHUP_MATRIX_LIMIT,
  selectLeaderMatchupMatrixLeaders,
} from "@/lib/leaderMatchupMatrixExport";
import type {
  LeaderLeaderboardItem,
  LeaderMatchupItem,
  MsgLeaderMatchupMatrix,
} from "@/types/net";

interface Props {
  data: MsgLeaderMatchupMatrix | null;
  leaderboardItems: LeaderLeaderboardItem[];
  onRetry: () => void;
}

function percent(value: number | null): string {
  return value == null ? "—" : `${(value * 100).toFixed(1)}%`;
}

function latestLeaderSprite(card: ReturnType<typeof getCard>): string {
  if (card?.sprites?.length) return card.sprites[card.sprites.length - 1];
  return card?.sprite ?? CARD_BACK_SRC;
}

function cellClasses(item?: LeaderMatchupItem): string {
  if (!item || item.isMirror) return "bg-gray-900/90 text-gray-600";
  if (item.winRate == null) return "bg-gray-950/80 text-gray-600";
  if (item.winRate >= 0.6) return "bg-emerald-500/15 text-emerald-300";
  if (item.winRate > 0.5) return "bg-green-500/10 text-green-300";
  if (item.winRate === 0.5) return "bg-slate-500/10 text-slate-200";
  if (item.winRate >= 0.4) return "bg-rose-500/10 text-rose-300";
  return "bg-red-500/15 text-red-300";
}

function positionText(
  label: "先" | "后",
  games: number | undefined,
  winRate: number | null | undefined,
): string {
  if (!games) return `${label} — · 0场`;
  return `${label} ${percent(winRate ?? null)} · ${games}场`;
}

function positionDetailText(
  label: "先" | "后",
  games: number | undefined,
  wins: number | undefined,
  winRate: number | null | undefined,
): string {
  return `${label} ${percent(winRate ?? null)}（${wins ?? 0}胜/${games ?? 0}场）`;
}

export default function LeaderMatchupMatrix({ data, leaderboardItems, onRetry }: Props) {
  const leaders = selectLeaderMatchupMatrixLeaders(leaderboardItems);
  const rowMap = new Map(data?.rows?.map((row) => [row.leaderNumber, row]) ?? []);
  const loading = data == null || data.result == null;

  if (loading) {
    return (
      <div className="grid min-h-64 place-items-center px-4 text-center">
        <div>
          <div className="mx-auto h-7 w-7 animate-spin rounded-full border-2 border-gray-800 border-t-orange-400" />
          <p className="mt-3 text-sm text-gray-500">正在生成对阵一图流…</p>
        </div>
      </div>
    );
  }

  if (data.result === false) {
    return (
      <div className="grid min-h-64 place-items-center px-4 text-center">
        <div>
          <p className="text-sm text-red-400">{data.error ?? "对阵矩阵暂时不可用"}</p>
          <button
            type="button"
            onClick={onRetry}
            className="mt-3 rounded-lg border border-gray-700 px-3 py-1.5 text-xs text-gray-300 transition-colors hover:border-orange-500 hover:text-white"
          >
            重试
          </button>
        </div>
      </div>
    );
  }

  if (leaders.length === 0) {
    return <p className="py-16 text-center text-sm text-gray-600">当前周期还没有满足排名门槛的 Leader</p>;
  }

  return (
    <section className="min-w-max">
      <header className="sticky left-0 z-20 flex w-[calc(100vw-2rem)] min-w-[760px] max-w-full flex-wrap items-center justify-between gap-2 border-b border-gray-800 bg-gray-950/95 px-4 py-3 backdrop-blur">
        <div>
          <h3 className="text-sm font-bold text-white">
            榜前 {LEADER_MATCHUP_MATRIX_LIMIT} 对阵一图流
            {leaders.length < LEADER_MATCHUP_MATRIX_LIMIT ? `（当前 ${leaders.length}）` : ""}
          </h3>
          <p className="mt-0.5 text-[11px] text-gray-600">横轴为对手，纵轴为我方；先/后均按纵轴我方视角，当前筛选档位最多展示 20 名</p>
        </div>
        <div className="flex flex-wrap items-center gap-3 text-[10px] text-gray-500">
          <span><i className="mr-1 inline-block h-2 w-2 rounded-sm bg-emerald-400/70" />优势</span>
          <span><i className="mr-1 inline-block h-2 w-2 rounded-sm bg-slate-400/70" />五五开</span>
          <span><i className="mr-1 inline-block h-2 w-2 rounded-sm bg-red-400/70" />劣势</span>
          <span>· 少于 5 场标记低样本</span>
        </div>
      </header>

      <table className="border-separate border-spacing-0 text-center">
        <thead>
          <tr>
            <th className="sticky left-0 top-0 z-40 w-32 min-w-32 border-b border-r border-gray-700 bg-gray-900 px-2 py-2 text-left align-bottom shadow-[4px_0_12px_rgba(0,0,0,0.2)]">
              <span className="block text-[10px] font-medium text-gray-600">我方 ↓</span>
              <span className="block text-xs font-bold text-gray-300">对手 →</span>
            </th>
            {leaders.map((leader) => {
              const card = getCard(leader.leaderNumber);
              const sprite = latestLeaderSprite(card);
              return (
                <th
                  key={leader.leaderNumber}
                  scope="col"
                  className="sticky top-0 z-30 w-24 min-w-24 border-b border-r border-gray-800 bg-gray-900 px-1 py-2 align-bottom"
                >
                  <div className="mx-auto">
                    <div className="relative mx-auto h-[62px] w-11 overflow-hidden rounded border border-gray-700 bg-gray-950">
                      <Image
                        src={thumbSrc(sprite)}
                        alt={card?.name ?? leader.leaderNumber}
                        fill
                        sizes="44px"
                        className="object-cover"
                        onError={(event) => advanceImageFallback(event.currentTarget, [sprite, card?.image])}
                      />
                    </div>
                    <p className="mt-1 truncate text-[9px] font-medium text-gray-400" title={card?.name ?? leader.leaderNumber}>
                      {leader.leaderNumber}
                    </p>
                    <p className="text-[10px] font-black text-orange-300">{percent(leader.winRate)}</p>
                  </div>
                </th>
              );
            })}
          </tr>
        </thead>
        <tbody>
          {leaders.map((leader) => {
            const card = getCard(leader.leaderNumber);
            const sprite = latestLeaderSprite(card);
            const row = rowMap.get(leader.leaderNumber);
            const cellMap = new Map(row?.items.map((item) => [item.leaderNumber, item]) ?? []);
            return (
              <tr key={leader.leaderNumber}>
                <th
                  scope="row"
                  className="sticky left-0 z-20 w-32 min-w-32 border-b border-r border-gray-700 bg-gray-900 px-2 py-1.5 text-left shadow-[4px_0_12px_rgba(0,0,0,0.2)]"
                >
                  <div className="flex items-center gap-2">
                    <span className="w-4 shrink-0 text-center text-[11px] font-black text-orange-400">{leader.rank}</span>
                    <div className="relative h-12 w-[34px] shrink-0 overflow-hidden rounded border border-gray-700 bg-gray-950">
                      <Image
                        src={thumbSrc(sprite)}
                        alt={card?.name ?? leader.leaderNumber}
                        fill
                        sizes="34px"
                        className="object-cover"
                        onError={(event) => advanceImageFallback(event.currentTarget, [sprite, card?.image])}
                      />
                    </div>
                    <div className="min-w-0">
                      <p className="truncate text-[10px] font-bold text-gray-200" title={card?.name ?? leader.leaderNumber}>
                        {card?.name ?? leader.leaderNumber}
                      </p>
                      <p className="text-[9px] text-gray-600">{leader.leaderNumber}</p>
                      <p className="text-[10px] font-black text-orange-300">{percent(leader.winRate)}</p>
                    </div>
                  </div>
                </th>
                {leaders.map((opponent) => {
                  const item = cellMap.get(opponent.leaderNumber);
                  const lowSample = Boolean(item && !item.isMirror && item.games > 0 && item.games < 5);
                  const firstLowSample = Boolean(item && item.firstGames > 0 && item.firstGames < 5);
                  const secondLowSample = Boolean(item && item.secondGames > 0 && item.secondGames < 5);
                  const rowName = card?.name ?? leader.leaderNumber;
                  const opponentName = getCard(opponent.leaderNumber)?.name ?? opponent.leaderNumber;
                  return (
                    <td
                      key={opponent.leaderNumber}
                      className={`h-[88px] w-24 min-w-24 border-b border-r border-gray-800 px-1 py-1.5 ${cellClasses(item)}`}
                      title={`${rowName} 对阵 ${opponentName}；${item?.isMirror ? `${item.games}场镜像` : `综合 ${percent(item?.winRate ?? null)}（${item?.wins ?? 0}胜/${item?.games ?? 0}场）`}；${positionDetailText("先", item?.firstGames, item?.firstWins, item?.firstWinRate)}；${positionDetailText("后", item?.secondGames, item?.secondWins, item?.secondWinRate)}`}
                    >
                      <p className="text-sm font-black tabular-nums">{item?.isMirror ? "镜像" : percent(item?.winRate ?? null)}</p>
                      <p className={`mt-0.5 text-[9px] tabular-nums ${lowSample ? "text-amber-400" : "text-gray-500"}`}>
                        {item?.isMirror
                          ? `${item.games} 场镜像`
                          : item && item.games > 0
                            ? `总 ${item.games}场${lowSample ? " · 低样本" : ""}`
                            : "暂无交手"}
                      </p>
                      <p className={`mt-1 text-[9px] font-semibold tabular-nums ${firstLowSample ? "text-amber-400" : "text-sky-300/90"}`}>
                        {positionText("先", item?.firstGames, item?.firstWinRate)}
                      </p>
                      <p className={`mt-0.5 text-[9px] font-semibold tabular-nums ${secondLowSample ? "text-amber-400" : "text-violet-300/90"}`}>
                        {positionText("后", item?.secondGames, item?.secondWinRate)}
                      </p>
                    </td>
                  );
                })}
              </tr>
            );
          })}
        </tbody>
      </table>
    </section>
  );
}
