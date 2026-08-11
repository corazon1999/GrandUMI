"use client";

import Image from "next/image";
import { getCard } from "@/data/CardLoader";
import { thumbSrc } from "@/lib/sprite";
import type { LeaderMatchupItem, MsgLeaderMatchups } from "@/types/net";

interface Props {
  data?: MsgLeaderMatchups;
  onRetry: () => void;
}

interface TierStyle {
  label: string;
  icon: string;
  className: string;
}

function percent(value: number | null): string {
  return value == null ? "—" : `${(value * 100).toFixed(1)}%`;
}

/** 与主榜一致，优先展示当前已收录的最后一张异画。 */
function latestLeaderSprite(card: ReturnType<typeof getCard>): string {
  if (card?.sprites?.length) return card.sprites[card.sprites.length - 1];
  return card?.sprite ?? "/sprites/CardBack.png";
}

function matchupTier(item: LeaderMatchupItem): TierStyle {
  if (item.isMirror) {
    return { label: "镜像", icon: "◆", className: "border-violet-500/30 bg-violet-500/10 text-violet-300" };
  }
  if (item.winRate == null) {
    return { label: "暂无数据", icon: "·", className: "border-gray-700 bg-gray-900 text-gray-500" };
  }
  if (item.winRate >= 0.6) {
    return { label: "大优", icon: "▲▲", className: "border-emerald-400/40 bg-emerald-400/10 text-emerald-300" };
  }
  if (item.winRate >= 0.55) {
    return { label: "优", icon: "▲", className: "border-green-500/30 bg-green-500/10 text-green-300" };
  }
  if (item.winRate >= 0.45) {
    return { label: "平", icon: "—", className: "border-slate-500/30 bg-slate-500/10 text-slate-300" };
  }
  if (item.winRate >= 0.4) {
    return { label: "小劣", icon: "▼", className: "border-amber-500/30 bg-amber-500/10 text-amber-300" };
  }
  return { label: "劣", icon: "▼▼", className: "border-red-500/40 bg-red-500/10 text-red-300" };
}

function StartingHandAnalysis({ data }: { data: MsgLeaderMatchups }) {
  const items = data.startingHandItems ?? [];
  const sampleGames = data.startingHandSampleGames ?? 0;

  return (
    <section className="mt-4 rounded-xl border border-sky-500/20 bg-slate-950/70 p-3 @[640px]:p-4">
      <header className="mb-3 flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
        <div>
          <h4 className="text-sm font-bold text-white">起手留牌前 10</h4>
          <p className="mt-0.5 text-[11px] text-gray-500">以双方完成换牌后的最终起手牌统计</p>
        </div>
        <p className="text-[11px] text-sky-200/70">已采样 {sampleGames} 场</p>
      </header>

      {items.length === 0 ? (
        <div className="grid min-h-24 place-items-center rounded-lg border border-dashed border-gray-800 px-4 text-center text-xs leading-5 text-gray-600">
          暂无起手留牌数据；完成的新对局会自动计入统计。
        </div>
      ) : (
        <div className="-mx-1 overflow-x-auto px-1 pb-1 [scrollbar-width:thin]">
          <ol className="flex min-w-max gap-2">
            {items.map((item, index) => {
              const card = getCard(item.cardNumber);
              const sprite = latestLeaderSprite(card);
              return (
                <li key={item.cardNumber} className="w-[76px] shrink-0 rounded-lg border border-gray-800 bg-gray-900/80 p-1.5 text-center">
                  <div className="relative mx-auto h-[86px] w-[60px] overflow-hidden rounded border border-gray-700 bg-gray-950">
                    <Image
                      src={thumbSrc(sprite)}
                      alt={card?.name ?? item.cardNumber}
                      fill
                      sizes="60px"
                      className="object-cover"
                    />
                  </div>
                  <p className="mt-1 truncate text-[10px] font-medium text-gray-300" title={card?.name ?? item.cardNumber}>
                    {index + 1}. {card?.name ?? item.cardNumber}
                  </p>
                  <p className="mt-0.5 text-xs font-black tabular-nums text-sky-200">{percent(item.percentage)}</p>
                  <p className="text-[9px] text-gray-600">{item.games} 场</p>
                </li>
              );
            })}
          </ol>
        </div>
      )}
    </section>
  );
}

export default function LeaderMatchupBreakdown({ data, onRetry }: Props) {
  const loading = data == null || data.result == null;

  return (
    <section className="rounded-xl border border-orange-500/20 bg-gray-950/90 p-3 @[640px]:p-4">
      <header className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="text-sm font-bold text-white">对阵当前排行榜前 20</h3>
          <p className="mt-0.5 text-[11px] text-gray-600">胜率按所选 Leader 的视角统计；少于 5 场会标记样本不足</p>
        </div>
        <p className="hidden text-[10px] text-gray-600 @[760px]:block">大优 ≥60% · 优 ≥55% · 平 45%–55% · 小劣 ≥40% · 劣 &lt;40%</p>
      </header>

      {loading ? (
        <div className="grid min-h-28 place-items-center rounded-lg border border-dashed border-gray-800 text-xs text-gray-600">
          正在统计对战数据…
        </div>
      ) : data.result === false ? (
        <div className="grid min-h-28 place-items-center rounded-lg border border-dashed border-red-500/20 px-4 text-center">
          <div>
            <p className="text-xs text-red-400">{data.error ?? "对战统计暂时不可用"}</p>
            <button
              type="button"
              onClick={onRetry}
              className="mt-2 rounded-lg border border-gray-700 px-3 py-1.5 text-xs text-gray-300 transition-colors hover:border-orange-500 hover:text-white"
            >
              重试
            </button>
          </div>
        </div>
      ) : (data.items?.length ?? 0) === 0 ? (
        <div className="grid min-h-28 place-items-center rounded-lg border border-dashed border-gray-800 text-xs text-gray-600">
          当前周期还没有满足排名门槛的 Leader
        </div>
      ) : (
        <>
          <div className="grid gap-2 @[760px]:grid-cols-2 @[1280px]:grid-cols-5">
            {data.items?.map((item) => {
            const card = getCard(item.leaderNumber);
            const tier = matchupTier(item);
            const lowSample = item.games > 0 && item.games < 5;
            return (
              <article key={item.leaderNumber} className="rounded-xl border border-gray-800 bg-gray-900/75 p-2.5">
                <div className="flex items-center gap-2">
                  <span className="w-5 shrink-0 text-center text-xs font-black text-orange-400">{item.rank}</span>
                  <div className="relative h-10 w-7 shrink-0 overflow-hidden rounded border border-gray-700 bg-gray-950">
                    <Image
                      src={thumbSrc(latestLeaderSprite(card))}
                      alt={card?.name ?? item.leaderNumber}
                      fill
                      sizes="28px"
                      className="object-cover"
                    />
                  </div>
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-xs font-bold text-gray-100">{card?.name ?? item.leaderNumber}</p>
                    <p className="text-[10px] text-gray-600">{item.leaderNumber}</p>
                  </div>
                </div>

                <div className="mt-2.5 flex items-center justify-between gap-2">
                  <div>
                    <p className="text-lg font-black text-white">{item.isMirror ? "—" : percent(item.winRate)}</p>
                    <p className="text-[10px] text-gray-600">
                      {item.isMirror
                        ? `${item.games} 场镜像对局`
                        : item.games === 0
                          ? "暂无交手"
                          : `${item.wins}-${item.losses} · ${item.games} 场`}
                    </p>
                  </div>
                  <div className="text-right">
                    <span className={`inline-flex items-center gap-1 rounded-md border px-2 py-1 text-[11px] font-bold ${tier.className}`}>
                      <span className="text-[9px]">{tier.icon}</span>
                      {tier.label}
                    </span>
                    {lowSample && <p className="mt-1 text-[9px] text-amber-500">样本不足</p>}
                  </div>
                </div>

                <div className="mt-2 grid grid-cols-2 divide-x divide-gray-800 rounded-lg bg-gray-950/70 py-1.5 text-center">
                  <div>
                    <p className="text-xs font-semibold text-gray-300">{percent(item.firstWinRate)}</p>
                    <p className="text-[9px] text-gray-600">先攻 · {item.firstGames} 场</p>
                  </div>
                  <div>
                    <p className="text-xs font-semibold text-gray-300">{percent(item.secondWinRate)}</p>
                    <p className="text-[9px] text-gray-600">后攻 · {item.secondGames} 场</p>
                  </div>
                </div>
              </article>
            );
            })}
          </div>
          <StartingHandAnalysis data={data} />
        </>
      )}
    </section>
  );
}
