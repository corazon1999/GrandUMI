"use client";

import NextImage from "next/image";
import { useEffect, useMemo, useState, type ReactNode } from "react";
import CardBack from "@/components/ui/CardBack";
import { getCard, loadCardSet } from "@/data/CardLoader";
import { CARD_BACK_OPTIONS, cardBackName, normalizeCardBackId, type CardBackId } from "@/lib/cardBacks";
import { advanceImageFallback, thumbSrc } from "@/lib/sprite";
import { HomeRequest } from "@/net/HomeProtocol";
import { eventBus } from "@/net/eventBus";
import { useNetStore } from "@/store/netStore";
import type { LeaderboardPeriod, MsgUpdatePs, PlayerLeaderStatsItem, RankFaction } from "@/types/net";

const PERIODS: Array<{ value: LeaderboardPeriod; label: string }> = [
  { value: "7d", label: "近 7 天" },
  { value: "30d", label: "近 30 天" },
  { value: "all", label: "全部" },
];

const RANK_FACTION_NAMES: Record<RankFaction, string> = {
  pirate: "海贼阵营",
  marine: "海军阵营",
  government: "世界政府阵营",
};

function rankLabel(tier: string, division: number | null, placementGames: number, placementRequired: number): string {
  if (placementGames < placementRequired) return `定级中 ${placementGames}/${placementRequired}`;
  return `${tier}${division ? ` ${["", "I", "II", "III"][division]}` : ""}`;
}

function dateLabel(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "—" : date.toLocaleDateString("zh-CN");
}

function percent(value: number | null | undefined): string {
  return value == null ? "—" : `${(value * 100).toFixed(1)}%`;
}

function latestLeaderSprite(card: ReturnType<typeof getCard>): string {
  if (card?.sprites?.length) return card.sprites[card.sprites.length - 1];
  return card?.sprite ?? "";
}

function maskedAccount(account: string): string {
  if (account.length <= 5) return account;
  return `${account.slice(0, 2)}•••${account.slice(-2)}`;
}

function LeaderPortrait({ item, featured = false }: { item: PlayerLeaderStatsItem; featured?: boolean }) {
  const card = getCard(item.leaderNumber);
  const sprite = latestLeaderSprite(card);
  return (
    <div className={`relative shrink-0 overflow-hidden bg-gray-950 ${featured ? "h-24 w-20 rounded-xl" : "h-14 w-11 rounded-lg"}`}>
      {sprite ? (
        <NextImage
          src={thumbSrc(sprite)}
          alt={card?.name ?? item.leaderNumber}
          fill
          sizes={featured ? "80px" : "44px"}
          className="object-cover"
          onError={(event) => advanceImageFallback(event.currentTarget, [sprite, card?.image])}
        />
      ) : (
        <span className="flex h-full items-center justify-center px-1 text-center text-[10px] font-bold text-gray-500">
          {item.leaderNumber}
        </span>
      )}
    </div>
  );
}

function CardBackPreview({ cardBackId }: { cardBackId: CardBackId }) {
  return (
    <div className="relative h-44 overflow-hidden rounded-2xl border border-gray-700 bg-[radial-gradient(circle_at_50%_120%,rgba(249,115,22,0.2),transparent_55%),linear-gradient(145deg,#111827,#030712)]">
      <div className="absolute inset-5 rounded-full border border-dashed border-gray-700/70" />
      <div className="absolute right-6 top-6 h-24 w-[4.25rem] rounded-lg shadow-2xl">
        <div className="absolute inset-0 translate-x-1.5 -translate-y-1.5 rounded-lg border border-gray-500 bg-gray-950" />
        <CardBack cardBackId={cardBackId} className="relative" />
      </div>
      <div className="absolute bottom-3 left-1/2 flex -translate-x-1/2 items-end">
        {[-10, -3, 3, 10].map((rotate, index) => (
          <div
            key={rotate}
            className={`h-20 w-14 rounded-md shadow-xl ${index > 0 ? "-ml-4" : ""}`}
            style={{ transform: `rotate(${rotate}deg) translateY(${Math.abs(rotate) / 2}px)` }}
          >
            <CardBack cardBackId={cardBackId} decorative />
          </div>
        ))}
      </div>
    </div>
  );
}

export default function ProfilePanel({
  profileEditor,
  onOpenPlayers,
  onOpenHistory,
  onOpenChangelog,
  onOpenSettings,
}: {
  profileEditor: ReactNode;
  onOpenPlayers: () => void;
  onOpenHistory: () => void;
  onOpenChangelog: () => void;
  onOpenSettings: () => void;
}) {
  const account = useNetStore((state) => state.account);
  const cloudCardBackId = useNetStore((state) => state.cardBackId);
  const stats = useNetStore((state) => state.playerProfileStats);
  const rankProfile = useNetStore((state) => state.rankProfile);
  const onlineCount = useNetStore((state) => state.onlineCount);
  const [period, setPeriod] = useState<LeaderboardPeriod>("30d");
  const [selectedLeaderNumber, setSelectedLeaderNumber] = useState("");
  const [selectedCardBackId, setSelectedCardBackId] = useState<CardBackId>(() => normalizeCardBackId(cloudCardBackId));
  const [savingCardBack, setSavingCardBack] = useState(false);
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [savingPassword, setSavingPassword] = useState(false);
  const [passwordError, setPasswordError] = useState("");
  const [, setCardVersion] = useState(0);

  useEffect(() => {
    HomeRequest.requestPlayerProfileStats(period);
  }, [period]);

  const topLeaders = useMemo(
    () => Array.isArray(stats?.topLeaders)
      ? stats.topLeaders.filter((item) => typeof item?.leaderNumber === "string" && item.leaderNumber.length > 0)
      : [],
    [stats?.topLeaders],
  );
  const trend = useMemo(
    () => Array.isArray(stats?.trend)
      ? stats.trend.filter((point) => typeof point?.label === "string")
      : [],
    [stats?.trend],
  );

  useEffect(() => {
    setSelectedLeaderNumber(topLeaders[0]?.leaderNumber ?? "");
    const setNames = [...new Set(topLeaders.map((item) => item.leaderNumber.split("-")[0]).filter(Boolean))];
    if (setNames.length === 0) return;
    let active = true;
    Promise.all(setNames.map((setName) => loadCardSet(setName).catch(() => undefined))).then(() => {
      if (active) setCardVersion((value) => value + 1);
    });
    return () => { active = false; };
  }, [topLeaders]);

  useEffect(() => {
    const normalized = normalizeCardBackId(cloudCardBackId);
    setSelectedCardBackId(normalized);
    setSavingCardBack(false);
  }, [cloudCardBackId]);

  useEffect(() => {
    const onMessage = (message: { proto: string }) => {
      if (message.proto !== "MsgUpdatePs") return;
      const result = message as MsgUpdatePs;
      setSavingPassword(false);
      if (result.result) {
        setCurrentPassword("");
        setNewPassword("");
        setConfirmPassword("");
        setPasswordError("");
      } else {
        setPasswordError(result.logStr ?? "密码修改失败，请重试。");
      }
    };
    eventBus.on("message", onMessage);
    return () => eventBus.off("message", onMessage);
  }, []);

  const selectedLeader = topLeaders.find((item) => item.leaderNumber === selectedLeaderNumber) ?? topLeaders[0];
  const selectedLeaderCard = selectedLeader ? getCard(selectedLeader.leaderNumber) : undefined;
  const selectedLeaderName = selectedLeaderCard?.name ?? selectedLeader?.leaderNumber ?? "暂无数据";
  const dataReady = stats?.result === true && stats.period === period;

  const saveCardBack = () => {
    if (selectedCardBackId === normalizeCardBackId(cloudCardBackId)) return;
    setSavingCardBack(true);
    if (!HomeRequest.updateCardBack(selectedCardBackId)) setSavingCardBack(false);
  };

  const changePassword = () => {
    setPasswordError("");
    if (!currentPassword) {
      setPasswordError("请输入当前密码。");
      return;
    }
    if (newPassword.length < 8 || newPassword.length > 128) {
      setPasswordError("新密码长度需为 8–128 个字符。");
      return;
    }
    if (newPassword !== confirmPassword) {
      setPasswordError("两次输入的新密码不一致。");
      return;
    }
    if (currentPassword === newPassword) {
      setPasswordError("新密码不能与当前密码相同。");
      return;
    }

    setSavingPassword(true);
    if (!HomeRequest.updatePassword(currentPassword, newPassword)) {
      setSavingPassword(false);
      setPasswordError("网络未连接，请稍后再试。");
    }
  };

  const shortcutClass = "flex min-h-20 items-center justify-between rounded-2xl border border-gray-800 bg-gray-900 p-4 text-left transition-colors hover:border-orange-600 active:bg-gray-800";

  return (
    <section className="h-full overflow-y-auto px-4 py-5 @[720px]:px-6 @[720px]:py-6">
      <header className="mb-4 flex items-end justify-between gap-4">
        <div>
          <h1 className="text-xl font-bold text-white @[720px]:text-2xl">个人详情</h1>
          <p className="mt-1 text-sm text-gray-500">战绩、常用领航与个性化设置</p>
        </div>
        <span className="hidden items-center gap-2 text-xs text-green-400 @[640px]:flex">
          <span className="h-2 w-2 rounded-full bg-green-400" />服务器在线
        </span>
      </header>

      <div className="relative overflow-hidden rounded-2xl border border-orange-500/30 bg-[radial-gradient(circle_at_90%_30%,rgba(249,115,22,0.22),transparent_30%),linear-gradient(120deg,#111827,#0b1120)] p-4 @[720px]:p-6">
        <div className="relative z-10 grid gap-5 @[720px]:grid-cols-[minmax(0,1fr)_auto] @[720px]:items-center">
          <div>
            {profileEditor}
            <div className="mt-3 flex flex-wrap items-center gap-2 text-xs text-gray-500">
              <span className="rounded-full border border-gray-700 bg-gray-950/50 px-2.5 py-1">账号 {maskedAccount(account)}</span>
              <span>资料与卡背均已云端同步</span>
            </div>
          </div>
          <div className="flex items-center gap-3 border-t border-gray-700/60 pt-4 @[720px]:border-l @[720px]:border-t-0 @[720px]:pl-5 @[720px]:pt-0">
            {selectedLeader ? <LeaderPortrait item={selectedLeader} featured /> : <div className="h-24 w-20 rounded-xl bg-gray-800" />}
            <div>
              <p className="text-xs text-orange-300">最爱玩的领航</p>
              <h2 className="mt-1 text-base font-bold text-white">{selectedLeaderName}</h2>
              <p className="mt-1 text-xs text-gray-500">{selectedLeader?.leaderNumber ?? "完成有效对局后生成"}</p>
            </div>
          </div>
        </div>
      </div>

      <article
        data-testid="profile-ranked-info"
        aria-labelledby="profile-rank-heading"
        className="mt-5 rounded-2xl border border-violet-800/60 bg-[radial-gradient(circle_at_100%_0%,rgba(124,58,237,0.18),transparent_38%),linear-gradient(135deg,rgba(46,16,101,0.36),rgba(17,24,39,0.96))] p-4 @[720px]:p-5"
      >
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h2 id="profile-rank-heading" className="text-lg font-bold text-white">排位信息</h2>
            <p className="mt-1 text-xs text-gray-400">展示当前赛季的阵营、段位、排位分与排位战绩</p>
          </div>
          {rankProfile && (
            <span className="rounded-full border border-violet-700/70 bg-violet-950/50 px-3 py-1 text-xs font-bold text-violet-200">
              {rankProfile.seasonId}
            </span>
          )}
        </div>

        {!rankProfile ? (
          <div className="mt-4 rounded-xl border border-dashed border-violet-800/60 bg-black/15 px-4 py-6 text-center text-sm text-gray-500">
            正在读取排位信息…
          </div>
        ) : !rankProfile.faction ? (
          <div className="mt-4 rounded-xl border border-dashed border-violet-700/60 bg-violet-950/20 px-4 py-5">
            <p className="font-bold text-violet-100">尚未选择排位阵营</p>
            <p className="mt-1 text-xs leading-5 text-gray-400">前往大厅的排位匹配选择阵营后，这里会显示你的当前段位与赛季战绩。</p>
          </div>
        ) : (
          <>
            <div className="mt-4 grid gap-2 @[560px]:grid-cols-3">
              <section className="rounded-xl border border-violet-600/60 bg-violet-950/40 p-4">
                <p className="text-xs font-bold text-violet-300">当前段位</p>
                <p className="mt-2 text-xl font-black text-white">
                  {rankLabel(rankProfile.tier, rankProfile.division, rankProfile.placementGames, rankProfile.placementRequired)}
                </p>
                <p className="mt-1 text-xs font-bold text-violet-300">
                  排位分 {rankProfile.rankPoints} RP
                </p>
              </section>
              <section className="rounded-xl border border-gray-800 bg-gray-950/45 p-4">
                <p className="text-xs text-gray-500">所属阵营</p>
                <p className="mt-2 text-base font-black text-violet-100">{RANK_FACTION_NAMES[rankProfile.faction]}</p>
              </section>
              <section className="rounded-xl border border-gray-800 bg-gray-950/45 p-4">
                <p className="text-xs text-gray-500">本赛季战绩</p>
                <p className="mt-2 text-base font-black text-white">{rankProfile.wins} 胜 / {rankProfile.losses} 负</p>
              </section>
            </div>
            <p className="mt-3 text-xs text-gray-500">赛季结束：{dateLabel(rankProfile.seasonEndsAtUtc)}</p>
          </>
        )}
      </article>

      <div className="mt-5 flex flex-col gap-3 @[640px]:flex-row @[640px]:items-end @[640px]:justify-between">
        <div>
          <h2 className="text-lg font-bold text-white">胜率概览</h2>
          <p className="mt-1 text-xs text-gray-500">统计真人有效对局，规则与 Leader 胜率榜一致</p>
        </div>
        <div className="grid grid-cols-3 rounded-xl border border-gray-800 bg-gray-900 p-1">
          {PERIODS.map((item) => (
            <button
              key={item.value}
              type="button"
              aria-pressed={period === item.value}
              onClick={() => setPeriod(item.value)}
              className={`min-h-9 rounded-lg px-3 text-xs font-bold transition-colors ${period === item.value ? "bg-orange-500 text-white" : "text-gray-500 hover:text-white"}`}
            >
              {item.label}
            </button>
          ))}
        </div>
      </div>

      <div className="mt-3 grid grid-cols-2 gap-3 @[720px]:grid-cols-4">
        {[
          ["总场次", dataReady ? stats.games ?? 0 : "—", "场"],
          ["胜场", dataReady ? stats.wins ?? 0 : "—", "胜"],
          ["负场", dataReady ? stats.losses ?? 0 : "—", "负"],
          ["综合胜率", dataReady ? percent(stats.winRate) : "—", ""],
        ].map(([label, value, unit], index) => (
          <div key={label} className={`rounded-2xl border p-4 ${index === 3 ? "border-orange-500/35 bg-orange-500/10" : "border-gray-800 bg-gray-900"}`}>
            <p className="text-xs text-gray-500">{label}</p>
            <p className={`mt-2 text-xl font-black ${index === 3 ? "text-orange-300" : "text-white"}`}>{value}<span className="ml-1 text-xs font-normal text-gray-600">{unit}</span></p>
          </div>
        ))}
      </div>

      <div className="mt-3 grid gap-3 @[960px]:grid-cols-[minmax(0,1.25fr)_minmax(18rem,0.75fr)]">
        <div className="grid content-start gap-3">
          <article className="rounded-2xl border border-gray-800 bg-gray-900 p-4 @[720px]:p-5">
            <div className="flex items-start justify-between gap-4">
              <div><h3 className="font-bold text-white">近期胜率趋势</h3><p className="mt-1 text-xs text-gray-500">空白日期表示没有符合条件的对局</p></div>
              <div className="text-right text-xs text-gray-500">
                <p className="text-sm font-bold text-orange-300">先攻 {dataReady ? percent(stats.firstWinRate) : "—"}</p>
                <p className="mt-1">后攻 {dataReady ? percent(stats.secondWinRate) : "—"}</p>
              </div>
            </div>
            <div className="mt-5 flex h-40 items-end gap-1.5 border-b border-gray-700 px-1" role="img" aria-label="个人胜率趋势图">
              {(dataReady ? trend : []).map((point) => {
                const height = point.winRate == null ? 5 : Math.max(12, point.winRate * 100);
                return (
                  <div key={point.label} className="flex h-full min-w-0 flex-1 items-end">
                    <div
                      className={`w-full rounded-t-sm ${point.winRate == null ? "bg-gray-800" : "bg-gradient-to-t from-orange-700 to-orange-300"}`}
                      style={{ height: `${height}%` }}
                      title={`${point.label} · ${point.games} 场 · 胜率 ${percent(point.winRate)}`}
                    />
                  </div>
                );
              })}
              {!dataReady && <div className="m-auto text-sm text-gray-600">正在读取统计…</div>}
              {dataReady && trend.length === 0 && <div className="m-auto text-sm text-gray-600">当前周期暂无有效对局</div>}
            </div>
            {dataReady && trend.length > 0 && (
              <div className="mt-2 flex justify-between text-[10px] text-gray-600"><span>{trend[0]?.label}</span><span>{trend.at(-1)?.label}</span></div>
            )}
          </article>

          <article className="rounded-2xl border border-gray-800 bg-gray-900 p-4 @[720px]:p-5">
            <div><h3 className="font-bold text-white">最爱玩的领航</h3><p className="mt-1 text-xs text-gray-500">按所选周期内使用场次自动排序</p></div>
            {selectedLeader ? (
              <>
                <div className="mt-4 flex items-center gap-4 border-b border-gray-800 pb-4">
                  <LeaderPortrait item={selectedLeader} featured />
                  <div className="min-w-0 flex-1">
                    <h4 className="truncate font-bold text-white">{selectedLeaderName}</h4>
                    <p className="mt-1 text-xs text-gray-500">{selectedLeader.leaderNumber}</p>
                    <div className="mt-3 grid grid-cols-3 gap-2 text-center">
                      <div><p className="font-bold text-white">{selectedLeader.games}</p><p className="text-[10px] text-gray-600">场次</p></div>
                      <div><p className="font-bold text-white">{percent(selectedLeader.usageRate)}</p><p className="text-[10px] text-gray-600">使用占比</p></div>
                      <div><p className="font-bold text-orange-300">{percent(selectedLeader.winRate)}</p><p className="text-[10px] text-gray-600">胜率</p></div>
                    </div>
                  </div>
                </div>
                <div className="mt-3 grid gap-2 @[560px]:grid-cols-3">
                  {topLeaders.map((item, index) => {
                    const card = getCard(item.leaderNumber);
                    const active = item.leaderNumber === selectedLeader.leaderNumber;
                    return (
                      <button
                        key={item.leaderNumber}
                        type="button"
                        aria-pressed={active}
                        onClick={() => setSelectedLeaderNumber(item.leaderNumber)}
                        className={`flex min-w-0 items-center gap-2 rounded-xl border p-2 text-left transition-colors ${active ? "border-orange-500 bg-orange-500/10" : "border-gray-800 bg-gray-950/50 hover:border-gray-600"}`}
                      >
                        <LeaderPortrait item={item} />
                        <span className="min-w-0"><span className="block truncate text-xs font-bold text-gray-200">{index + 1}. {card?.name ?? item.leaderNumber}</span><span className="mt-1 block text-[10px] text-gray-600">{item.games} 场 · {percent(item.winRate)}</span></span>
                      </button>
                    );
                  })}
                </div>
              </>
            ) : (
              <div className="mt-4 rounded-xl border border-dashed border-gray-700 py-10 text-center text-sm text-gray-600">完成一场真人有效对局后，这里会自动生成偏好</div>
            )}
          </article>
        </div>

        <div className="grid content-start gap-3">
          <article className="rounded-2xl border border-gray-800 bg-gray-900 p-4 @[720px]:p-5">
            <div className="flex items-start justify-between gap-3"><div><h3 className="font-bold text-white">卡背设置</h3><p className="mt-1 text-xs text-gray-500">应用于手牌、主卡组和生命区</p></div><span className="rounded-full border border-gray-700 px-2 py-1 text-[10px] text-gray-500">云端同步</span></div>
            <div className="mt-4"><CardBackPreview cardBackId={selectedCardBackId} /></div>
            <div className="mt-3 grid grid-cols-2 gap-2">
              {CARD_BACK_OPTIONS.map((option) => {
                const active = selectedCardBackId === option.id;
                return (
                  <button
                    key={option.id}
                    type="button"
                    aria-pressed={active}
                    onClick={() => setSelectedCardBackId(option.id)}
                    className={`flex min-h-16 items-center gap-2 rounded-xl border p-2 text-left transition-colors ${active ? "border-orange-500 bg-orange-500/10" : "border-gray-800 bg-gray-950/50 hover:border-gray-600"}`}
                  >
                    <span className="h-12 w-9 shrink-0 rounded"><CardBack cardBackId={option.id} decorative /></span>
                    <span className="min-w-0"><span className="block text-xs font-bold text-gray-200">{option.name}</span><span className="mt-1 block truncate text-[10px] text-gray-600">{option.description}</span></span>
                  </button>
                );
              })}
            </div>
            <div className="mt-4 flex items-center justify-between gap-3">
              <p className="text-xs text-gray-500">当前：{cardBackName(cloudCardBackId)}</p>
              <button
                type="button"
                onClick={saveCardBack}
                disabled={savingCardBack || selectedCardBackId === normalizeCardBackId(cloudCardBackId)}
                className="min-h-11 rounded-xl bg-orange-500 px-4 text-sm font-bold text-white transition-colors hover:bg-orange-400 disabled:cursor-default disabled:bg-gray-700 disabled:text-gray-500"
              >
                {savingCardBack ? "保存中…" : selectedCardBackId === normalizeCardBackId(cloudCardBackId) ? "已保存" : "保存卡背"}
              </button>
            </div>
          </article>

          <article className="rounded-2xl border border-gray-800 bg-gray-900 p-4 @[720px]:p-5">
            <div className="flex items-start justify-between gap-3">
              <div>
                <h3 className="font-bold text-white">账户安全</h3>
                <p className="mt-1 text-xs text-gray-500">修改后，其他已登录会话将失效</p>
              </div>
              <span className="rounded-full border border-green-900/70 bg-green-950/30 px-2 py-1 text-[10px] text-green-400">密码已保护</span>
            </div>
            <div className="mt-4 grid gap-3">
              <label className="grid gap-1.5 text-xs font-medium text-gray-400">
                当前密码
                <input
                  type="password"
                  value={currentPassword}
                  onChange={(event) => setCurrentPassword(event.target.value)}
                  autoComplete="current-password"
                  maxLength={128}
                  className="h-11 rounded-xl border border-gray-700 bg-gray-950 px-3 text-sm text-white outline-none focus:border-orange-500 focus-visible:ring-2 focus-visible:ring-orange-500/30"
                />
              </label>
              <div className="grid gap-3 @[560px]:grid-cols-2">
                <label className="grid gap-1.5 text-xs font-medium text-gray-400">
                  新密码
                  <input
                    type="password"
                    value={newPassword}
                    onChange={(event) => setNewPassword(event.target.value)}
                    autoComplete="new-password"
                    maxLength={128}
                    placeholder="8–128 个字符"
                    className="h-11 rounded-xl border border-gray-700 bg-gray-950 px-3 text-sm text-white outline-none placeholder:text-gray-700 focus:border-orange-500 focus-visible:ring-2 focus-visible:ring-orange-500/30"
                  />
                </label>
                <label className="grid gap-1.5 text-xs font-medium text-gray-400">
                  确认新密码
                  <input
                    type="password"
                    value={confirmPassword}
                    onChange={(event) => setConfirmPassword(event.target.value)}
                    onKeyDown={(event) => event.key === "Enter" && changePassword()}
                    autoComplete="new-password"
                    maxLength={128}
                    className="h-11 rounded-xl border border-gray-700 bg-gray-950 px-3 text-sm text-white outline-none focus:border-orange-500 focus-visible:ring-2 focus-visible:ring-orange-500/30"
                  />
                </label>
              </div>
            </div>
            {passwordError && <p role="alert" className="mt-3 text-xs text-red-300">{passwordError}</p>}
            <div className="mt-4 flex justify-end">
              <button
                type="button"
                onClick={changePassword}
                disabled={savingPassword}
                className="min-h-11 rounded-xl bg-orange-500 px-4 text-sm font-bold text-white transition-colors hover:bg-orange-400 disabled:cursor-wait disabled:bg-gray-700 disabled:text-gray-500"
              >
                {savingPassword ? "修改中…" : "修改密码"}
              </button>
            </div>
          </article>

          <article className="rounded-2xl border border-gray-800 bg-gray-900 p-4">
            <h3 className="font-bold text-white">更多</h3>
            <div className="mt-3 grid grid-cols-2 gap-2">
              <button type="button" onClick={onOpenHistory} className={shortcutClass}><span><strong className="block text-orange-300">战绩</strong><span className="mt-1 block text-xs text-gray-500">对局记录</span></span><span className="text-gray-600">›</span></button>
              <button type="button" onClick={onOpenPlayers} className={shortcutClass}><span><strong className="block text-green-400">{onlineCount}</strong><span className="mt-1 block text-xs text-gray-500">在线玩家</span></span><span className="text-gray-600">›</span></button>
              <button type="button" onClick={onOpenSettings} className={shortcutClass}><span><strong className="block text-gray-200">设置</strong><span className="mt-1 block text-xs text-gray-500">界面布局</span></span><span className="text-gray-600">›</span></button>
              <button type="button" onClick={onOpenChangelog} className={shortcutClass}><span><strong className="block text-gray-200">日志</strong><span className="mt-1 block text-xs text-gray-500">最近更新</span></span><span className="text-gray-600">›</span></button>
            </div>
          </article>
        </div>
      </div>
    </section>
  );
}
