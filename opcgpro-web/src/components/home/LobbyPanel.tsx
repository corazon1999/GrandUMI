"use client";

import { useEffect, useState } from "react";
import { useNetStore } from "@/store/netStore";
import { HomeRequest } from "@/net/HomeProtocol";
import { showMessage } from "@/components/ui/MessageBox";
import Modal from "@/components/ui/Modal";
import { LeaderChampionBadgeList } from "@/components/ui/LeaderChampionBadge";
import RankTierBadge from "@/components/ui/RankTierBadge";
import ChatPanel from "./ChatPanel";
import SpectateSettingsPanel from "./SpectateSettingsPanel";
import { advanceImageFallback, CARD_BACK_SRC, thumbSrc } from "@/lib/sprite";
import { formatRankBounty } from "@/lib/rankBounty";
import type { RankFaction, RankedMode } from "@/types/net";

const RANK_FACTIONS: ReadonlyArray<{ id: RankFaction; name: string; titles: readonly string[]; className: string }> = [
  { id: "pirate", name: "海贼阵营", titles: ["见习海贼", "海贼战斗员", "海贼干部", "副船长", "船长"], className: "border-rose-700/70 bg-rose-950/30 hover:border-rose-400" },
  { id: "marine", name: "海军阵营", titles: ["海军三等兵", "海军少尉", "海军少校", "海军少将", "海军中将"], className: "border-sky-700/70 bg-sky-950/30 hover:border-sky-400" },
  { id: "government", name: "世界政府阵营", titles: ["政府线人", "初级特工", "CP9 特工", "CP0 特工", "浅海契约"], className: "border-amber-700/70 bg-amber-950/30 hover:border-amber-400" },
];

const RANK_FACTION_NAMES: Record<RankFaction, string> = {
  pirate: "海贼阵营",
  marine: "海军阵营",
  government: "世界政府阵营",
};

function RankFactionRules({ currentFaction }: { currentFaction?: RankFaction | null }) {
  return (
    <div id="rank-faction-rules" className="mt-2 rounded-xl border border-violet-800/60 bg-black/20 p-3 text-xs leading-5 text-gray-300">
      <p className="font-bold text-violet-200">阵营只改变排位称号，不影响匹配范围或悬赏金结算。</p>
      <div className="mt-3 grid gap-2 @[640px]:grid-cols-3">
        {RANK_FACTIONS.map((faction) => (
          <section
            key={faction.id}
            className={`rounded-lg border p-2.5 ${faction.id === currentFaction ? "border-violet-400 bg-violet-950/40" : "border-gray-800 bg-gray-950/60"}`}
          >
            <h3 className="font-black text-white">{faction.name}{faction.id === currentFaction ? "（当前）" : ""}</h3>
            <p className="mt-1 text-gray-400">{faction.titles.join("、")}</p>
          </section>
        ))}
      </div>
      <ul className="mt-3 list-disc space-y-1 pl-4 text-gray-400">
        <li>先完成 5 场定级赛；定级结果最高为各阵营第三阶 I。</li>
        <li>每个称号分 III、II、I 三个小段；悬赏金每增加 1000万贝里变化一个小段，每增加 3000万贝里进入下一称号。</li>
        <li>悬赏金未达到 1亿5000万贝里时，基础胜负会增加或减少 200万贝里；11 连胜起奖励封顶 100万贝里，6 连败起保护封顶 50万贝里。</li>
        <li>低悬赏方每低 1000万贝里多加或少扣 10万贝里；高悬赏方每高 1000万贝里少加或多扣 10万贝里。未达到 1亿5000万贝里时，两者最多修正 50万贝里。</li>
        <li>悬赏金达到 1亿5000万但未达到 3亿贝里时，基础胜负增加或减少 400万贝里；连胜奖励、连败保护和分差修正上限分别最高为 200万、100万和 100万贝里。</li>
        <li>悬赏金达到 3亿但未达到 6亿贝里时，基础胜负增加或减少 800万贝里；连胜奖励、连败保护和分差修正上限分别最高为 400万、200万和 200万贝里。</li>
        <li>悬赏金达到 6亿但未达到 10亿贝里时，基础胜负增加或减少 1500万贝里；连胜奖励最高 750万贝里，连败保护和分差修正上限均为 380万贝里。</li>
        <li>悬赏金达到 10亿贝里后，基础胜负增加或减少 2500万贝里；连胜奖励最高 1250万贝里，连败保护和分差修正上限均为 630万贝里。</li>
        <li>终结至少 3 连胜的玩家时，会按对方赛前悬赏金档位额外获得一次连胜赏金：1亿5000万、3亿、6亿和10亿档分别为 200万、400万、750万和1250万贝里。</li>
        <li>悬赏金达到 1亿5000万、3亿、6亿或 10亿贝里后，对应档位会成为永久保底线，之后不会再掉回上一档。</li>
        <li>第一阶不因失败降低显示悬赏金，第二、三阶拥有大段保护；每局结算会逐项展示悬赏金变化。</li>
        <li>进入新世界后，海贼、海军、世界政府普通玩家分别显示超新星、大将候补、神之骑士团；阵营前列玩家会获得海贼王、四皇、海军元帅、海军大将、世界之王或五老星称号。</li>
        <li>更换阵营会清空本赛季悬赏金、定级进度和战绩，并重新开始定级。</li>
      </ul>
    </div>
  );
}

// 从导出卡组码（exportDeckString 格式 A）统计主卡组张数：
// 卡牌行形如「<数量> <卡号>」，跳过「# 注释」与「领航:」行。
// 解析不出（返回 0）时按「未知」处理，调用方 fail-open，避免误拦合法卡组。
function countMainCards(deckStr: string): number {
  let total = 0;
  for (const line of deckStr.split("\n")) {
    const m = line.match(/^\s*(\d+)\s+[A-Za-z]/);
    if (m) total += parseInt(m[1], 10);
  }
  return total;
}

export default function LobbyPanel({ onGoToDeck }: { onGoToDeck: () => void }) {
  const matchState    = useNetStore((s) => s.matchState);
  const selectedDeck  = useNetStore((s) => s.selectedDeck);
  const opponentName  = useNetStore((s) => s.opponentName);
  const matchQueueKind = useNetStore((s) => s.matchQueueKind);
  const rankedMode: RankedMode = matchQueueKind === "rankedWild" || matchQueueKind === "casual" ? "wild" : "standard";
  const rankProfile = useNetStore((s) => s.rankProfiles[rankedMode]);
  const playerName    = useNetStore((s) => s.playerName);
  const roomCode      = useNetStore((s) => s.roomCode);
  const roomOperation = useNetStore((s) => s.roomOperation);
  const connState     = useNetStore((s) => s.connState);
  const maintenance   = useNetStore((s) => s.maintenance);

  const [roomMode, setRoomMode] = useState<"none" | "create" | "join">("none");
  const [playMode, setPlayMode] = useState<"match" | "friend" | "bot">("match");
  const [joinInput, setJoinInput] = useState("");
  const [copied, setCopied] = useState(false);
  const [botGoFirst, setBotGoFirst] = useState(true);
  const [pendingFaction, setPendingFaction] = useState<RankFaction | null>(null);
  const [factionEditorOpen, setFactionEditorOpen] = useState(false);
  const [rankRulesOpen, setRankRulesOpen] = useState(false);

  // 主卡组须恰好 50 张（后端 DeckValidator 强制，不满会被拒，bug #183）。
  // 这里前置拦截：未满 50 时置灰按钮并提示，避免「点了没反应」。
  // mainCount === 0 表示解析失败/未知，fail-open 不拦截，交由后端校验兜底。
  const mainCount     = selectedDeck ? countMainCards(selectedDeck.cards) : 0;
  const deckIncomplete = mainCount > 0 && mainCount !== 50;
  const canEnter      = !!selectedDeck && !deckIncomplete && connState === "connected" && roomOperation === "idle" && !maintenance.enabled;
  const isRanked = matchQueueKind === "ranked" || matchQueueKind === "rankedWild";
  const matchQueueLabel = matchQueueKind === "rankedWild"
    ? "狂野排位"
    : matchQueueKind === "ranked"
      ? "标准排位"
      : matchQueueKind === "casual"
        ? "狂野休闲"
        : "标准休闲";
  const canQueue = canEnter && (!isRanked || Boolean(rankProfile?.faction));

  useEffect(() => {
    if (isRanked && !rankProfile) HomeRequest.requestRankSnapshot(rankedMode);
  }, [isRanked, rankProfile, rankedMode]);

  useEffect(() => {
    if (roomMode === "create" && roomOperation === "idle" && !roomCode) {
      setRoomMode("none");
    }
  }, [roomCode, roomMode, roomOperation]);

  const handleMatch = () => {
    if (!selectedDeck) return;
    if (isRanked && !rankProfile?.faction) {
      showMessage("开始排位前请先选择阵营", "error");
      return;
    }
    const sent = HomeRequest.enterMatch(selectedDeck.cards, selectedDeck.name, matchQueueKind);
    if (!sent) {
      showMessage("服务器未连接，请稍后重试", "error");
    }
  };

  const requestFactionChange = (faction: RankFaction) => {
    if (!rankProfile?.faction) {
      HomeRequest.selectRankFaction(faction, false, rankedMode);
      return;
    }
    if (rankProfile.faction !== faction) setPendingFaction(faction);
  };

  const confirmFactionChange = () => {
    if (!pendingFaction) return;
    HomeRequest.selectRankFaction(pendingFaction, true, rankedMode);
    setPendingFaction(null);
  };

  const handleCancelMatch = () => {
    HomeRequest.cancelMatch();
  };

  // 单人测试：与机器人对战，方便测试单卡效果（机器人轮到自己会自动结束回合）
  const handleBotMatch = () => {
    if (!selectedDeck) return;
    const sent = HomeRequest.enterBotMatch(selectedDeck.cards, botGoFirst, selectedDeck.name);
    if (!sent) {
      showMessage("服务器未连接，请稍后重试", "error");
    }
  };

  const handleCreateRoom = () => {
    if (!selectedDeck) return;
    setRoomMode("create");
    useNetStore.getState().setRoomOperation("creating");
    const sent = HomeRequest.createRoom(selectedDeck.cards, selectedDeck.name);
    if (!sent) {
      showMessage("服务器未连接，请稍后重试", "error");
      useNetStore.getState().setRoomOperation("idle");
      setRoomMode("none");
    }
  };

  const handleJoinRoom = () => {
    if (!selectedDeck) return;
    setRoomMode("join");
  };

  const confirmJoinRoom = () => {
    const code = joinInput.trim().toUpperCase();
    if (code.length < 6 || !selectedDeck) return;
    useNetStore.getState().setRoomOperation("joining");
    const sent = HomeRequest.joinRoom(code, selectedDeck.cards, selectedDeck.name);
    if (!sent) {
      useNetStore.getState().setRoomOperation("idle");
      showMessage("服务器未连接，房间码已保留，请重连后重试", "error");
    }
  };

  const handleCancelRoom = () => {
    HomeRequest.cancelRoom();
    useNetStore.getState().setRoomOperation("idle");
    setRoomMode("none");
    setJoinInput("");
  };

  const copyRoomCode = () => {
    if (!roomCode) return;
    navigator.clipboard.writeText(roomCode).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }).catch(() => {});
  };

  const modeLocked = matchState !== "idle" || roomOperation !== "idle" || Boolean(roomCode);
  const entryHint = !selectedDeck
    ? "请先选择一副卡组"
    : deckIncomplete
      ? `卡组需正好 50 张，当前 ${mainCount} 张`
      : maintenance.enabled
        ? "维护更新中，暂时无法开始新的对局"
      : connState !== "connected"
        ? "服务器连接恢复后即可开始"
        : "";

  return (
    <div className="flex h-full min-w-0">
      <div className="min-h-0 min-w-0 flex-1 overflow-y-auto px-4 py-3 @[640px]:px-6 @[640px]:py-5 @[1024px]:flex @[1024px]:flex-col @[1024px]:items-center @[1024px]:px-8 @[1024px]:py-8">
        <div className="mx-auto flex w-full max-w-xl flex-col gap-3 @[640px]:gap-4 @[1024px]:my-auto @[1024px]:gap-5">
          <div>
            <h1 className="text-xl font-bold text-white @[1024px]:text-2xl">开始对战</h1>
            <p className="mt-1 text-sm text-gray-500 [@media(max-height:800px)]:hidden">选择模式，准备好后即可进入牌桌。</p>
          </div>

          <button
            type="button"
            onClick={onGoToDeck}
            className="w-full rounded-2xl border border-gray-800 bg-gray-900 p-3 text-left transition-colors hover:border-orange-700 active:bg-gray-800 @[640px]:p-4"
          >
            {selectedDeck ? (
              <div className="flex items-center gap-3">
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img
                  src={thumbSrc(selectedDeck.leaderSprite || CARD_BACK_SRC)}
                  alt={selectedDeck.leaderName}
                  className="h-16 w-11 shrink-0 rounded-lg border border-gray-700 object-cover"
                  onError={(e) => advanceImageFallback(e.currentTarget, [selectedDeck.leaderSprite])}
                />
                <div className="min-w-0 flex-1">
                  <p className="text-sm text-gray-500">当前卡组</p>
                  <p className="mt-0.5 truncate text-base font-bold text-white">{selectedDeck.name}</p>
                  <p className="mt-0.5 truncate text-sm text-gray-500">领航：{selectedDeck.leaderName}</p>
                </div>
                <span className="shrink-0 text-sm font-medium text-orange-300">更换 ›</span>
              </div>
            ) : (
              <div className="flex min-h-16 items-center justify-between gap-3">
                <div>
                  <p className="font-bold text-white">还没有选择卡组</p>
                  <p className="mt-1 text-sm text-gray-500">先准备一副完整的 50 张卡组</p>
                </div>
                <span className="shrink-0 rounded-lg bg-orange-500 px-3 py-2 text-sm font-bold text-white">去选择</span>
              </div>
            )}
          </button>

          {deckIncomplete && (
            <p role="alert" className="rounded-xl border border-red-900/70 bg-red-950/30 px-3 py-2.5 text-sm text-red-300">
              卡组需正好 50 张，当前 {mainCount} 张。请补满后再开始对战。
            </p>
          )}

          <div className="grid grid-cols-3 rounded-xl border border-gray-800 bg-gray-900 p-1" aria-label="对战模式">
            {([
              ["match", "匹配"],
              ["friend", "好友房"],
              ["bot", "单人"],
            ] as const).map(([mode, label]) => (
              <button
                key={mode}
                type="button"
                onClick={() => setPlayMode(mode)}
                disabled={modeLocked && playMode !== mode}
                aria-pressed={playMode === mode}
                className={`min-h-11 rounded-lg px-2 text-sm font-bold transition-colors ${
                  playMode === mode ? "bg-orange-500 text-white" : "text-gray-500 hover:bg-gray-800 hover:text-gray-200 disabled:opacity-40"
                }`}
              >
                {label}
              </button>
            ))}
          </div>

          <section className="rounded-2xl border border-gray-800 bg-gray-900 p-3 @[640px]:p-5">
            {playMode === "match" && (
              <div className="flex flex-col gap-3">
                {matchState === "idle" && (
                  <>
                    <div>
                      <h2 className="font-bold text-white">公开匹配</h2>
                      <p className="mt-1 text-sm leading-5 text-gray-500">排位会改变悬赏金，休闲不影响排名。</p>
                    </div>
                    <div className="grid grid-cols-2 rounded-xl border border-gray-800 bg-gray-950 p-1" aria-label="公开匹配类型">
                      <button
                        type="button"
                        onClick={() => useNetStore.getState().setMatchQueueKind(rankedMode === "wild" ? "rankedWild" : "ranked")}
                        aria-pressed={isRanked}
                        className={`min-h-11 rounded-lg px-3 text-sm font-black transition-colors ${isRanked ? "bg-violet-600 text-white" : "text-gray-500 hover:bg-gray-800 hover:text-gray-200"}`}
                      >
                        排位匹配
                      </button>
                      <button
                        type="button"
                        onClick={() => useNetStore.getState().setMatchQueueKind(rankedMode === "wild" ? "casual" : "casualStandard")}
                        aria-pressed={!isRanked}
                        className={`min-h-11 rounded-lg px-3 text-sm font-black transition-colors ${!isRanked ? "bg-orange-500 text-white" : "text-gray-500 hover:bg-gray-800 hover:text-gray-200"}`}
                      >
                        休闲匹配
                      </button>
                    </div>

                    <div className="grid grid-cols-2 rounded-xl border border-violet-900/70 bg-gray-950 p-1" aria-label={isRanked ? "排位模式" : "休闲模式"}>
                        <button
                          type="button"
                          onClick={() => useNetStore.getState().setMatchQueueKind(isRanked ? "ranked" : "casualStandard")}
                          aria-pressed={rankedMode === "standard"}
                          className={`min-h-11 rounded-lg px-3 text-sm font-black transition-colors ${rankedMode === "standard" ? "bg-violet-600 text-white" : "text-gray-500 hover:bg-gray-800 hover:text-gray-200"}`}
                        >
                          标准
                        </button>
                        <button
                          type="button"
                          onClick={() => useNetStore.getState().setMatchQueueKind(isRanked ? "rankedWild" : "casual")}
                          aria-pressed={rankedMode === "wild"}
                          className={`min-h-11 rounded-lg px-3 text-sm font-black transition-colors ${rankedMode === "wild" ? "bg-fuchsia-600 text-white" : "text-gray-500 hover:bg-gray-800 hover:text-gray-200"}`}
                        >
                          狂野
                        </button>
                      </div>
                    <p className="text-xs leading-5 text-gray-500">
                      {rankedMode === "standard"
                        ? `${isRanked ? "标准排位" : "标准休闲"}遵循当前环境禁限卡表。`
                        : isRanked
                          ? "狂野排位可使用角标 1 等已轮换卡牌，但仍执行官网禁卡表；禁卡仅好友或房间对战可用。"
                          : "狂野休闲可使用角标 1 等已轮换卡牌，但仍执行官网禁卡表；禁卡仅好友或房间对战可用。"}
                    </p>

                    {isRanked && rankProfile && (
                      <div className="rounded-xl border border-violet-800/70 bg-violet-950/25 p-3">
                        {!rankProfile.faction ? (
                          <div>
                            <p className="text-sm font-black text-violet-200">选择你的排位阵营</p>
                            <p className="mt-1 text-xs leading-5 text-gray-400">阵营只影响称号和阵营榜名次，不影响悬赏金结算或匹配；之后可更换，但会清空本赛季排位进度。</p>
                            <div className="mt-3 grid gap-2 @[640px]:grid-cols-3">
                              {RANK_FACTIONS.map((faction) => (
                                <button
                                  key={faction.id}
                                  type="button"
                                  onClick={() => requestFactionChange(faction.id)}
                                  className={`min-h-16 rounded-lg border px-3 py-2 text-left transition-colors ${faction.className}`}
                                >
                                  <span className="block text-sm font-black text-white">{faction.name}</span>
                                  <span className="mt-1 block text-[11px] text-gray-300">完成定级后获得对应阵营称号</span>
                                </button>
                              ))}
                            </div>
                          </div>
                        ) : (
                          <>
                            <div>
                              <p className="text-xs font-bold text-violet-300">{RANK_FACTION_NAMES[rankProfile.faction]} · {rankProfile.seasonId}</p>
                              <p className="mt-1 text-[11px] font-bold text-gray-500">当前段位</p>
                              <div className="mt-0.5 flex min-h-7 min-w-0 flex-wrap items-center gap-2">
                                {rankProfile.placementGames < rankProfile.placementRequired
                                  ? <span className="text-lg font-black text-white">定级中 {rankProfile.placementGames}/{rankProfile.placementRequired}</span>
                                  : <RankTierBadge faction={rankProfile.faction} tier={rankProfile.tier} division={rankProfile.division} className="text-sm" />}
                                <LeaderChampionBadgeList leaderNumbers={rankProfile.championLeaderNumbers} maxVisible={2} />
                              </div>
                              <p className="mt-1 text-xs font-bold text-violet-300">悬赏金 {formatRankBounty(rankProfile.rankPoints)}</p>
                            </div>
                            <p className="mt-2 text-xs text-gray-500">战绩 {rankProfile.wins} 胜 / {rankProfile.losses} 负 · 赛季结束 {new Date(rankProfile.seasonEndsAtUtc).toLocaleDateString("zh-CN")}</p>
                            <div className="mt-2 grid grid-cols-2 border-y border-violet-900/60" aria-label="排位阵营操作">
                              <button
                                type="button"
                                aria-expanded={factionEditorOpen}
                                aria-controls="rank-faction-editor"
                                onClick={() => {
                                  setFactionEditorOpen((open) => !open);
                                  setRankRulesOpen(false);
                                }}
                                className="min-h-11 px-2 text-left text-sm font-bold text-violet-300 transition-colors hover:bg-violet-900/30"
                              >
                                更换阵营
                              </button>
                              <button
                                type="button"
                                aria-expanded={rankRulesOpen}
                                aria-controls="rank-faction-rules"
                                onClick={() => {
                                  setRankRulesOpen((open) => !open);
                                  setFactionEditorOpen(false);
                                }}
                                className="min-h-11 border-l border-violet-900/60 px-2 text-left text-sm font-bold text-violet-300 transition-colors hover:bg-violet-900/30"
                              >
                                阵营规则
                              </button>
                            </div>
                            {factionEditorOpen && (
                              <div id="rank-faction-editor" className="mt-2">
                                <p className="mb-2 text-xs leading-5 text-gray-400">更换后将清空本赛季悬赏金、定级进度和战绩，并从头定级。</p>
                                <div className="grid gap-2 @[640px]:grid-cols-3">
                                  {RANK_FACTIONS.map((faction) => (
                                    <button
                                      key={faction.id}
                                      type="button"
                                      disabled={faction.id === rankProfile.faction}
                                      onClick={() => requestFactionChange(faction.id)}
                                      className={`min-h-16 rounded-lg border px-3 py-2 text-left transition-colors disabled:cursor-not-allowed disabled:opacity-45 ${faction.className}`}
                                    >
                                      <span className="block text-sm font-black text-white">{faction.id === rankProfile.faction ? `${faction.name}（当前）` : faction.name}</span>
                                      <span className="mt-1 block text-[11px] text-gray-300">{faction.id === rankProfile.faction ? `当前段位：${rankProfile.tier}${rankProfile.division ? ` ${["", "I", "II", "III"][rankProfile.division]}` : ""}` : "更换后重新参加定级赛"}</span>
                                    </button>
                                  ))}
                                </div>
                              </div>
                            )}
                            {rankRulesOpen && <RankFactionRules currentFaction={rankProfile.faction} />}
                          </>
                        )}
                      </div>
                    )}
                    <button
                      type="button"
                      onClick={handleMatch}
                      disabled={!canQueue}
                      className={`h-12 w-full rounded-xl text-base font-bold text-white transition-colors disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-600 ${isRanked ? "bg-violet-600 hover:bg-violet-500 active:bg-violet-700" : "bg-orange-500 hover:bg-orange-400 active:bg-orange-600"}`}
                    >
                      开始{matchQueueLabel}匹配
                    </button>
                  </>
                )}
                {matchState === "matching" && (
                  <div className="flex flex-col items-center gap-3 py-2" role="status">
                    <div className="h-6 w-6 animate-spin rounded-full border-2 border-orange-500 border-t-transparent" />
                    <p className="font-bold text-orange-300">正在寻找{matchQueueLabel}对手…</p>
                    <button type="button" onClick={handleCancelMatch} className="min-h-11 rounded-lg px-4 text-sm text-gray-400 hover:bg-gray-800 hover:text-white">
                      取消匹配
                    </button>
                  </div>
                )}
                {matchState === "matched" && (
                  <div className="py-2 text-center" role="status">
                    <p className="font-bold text-green-400">匹配成功</p>
                    <p className="mt-2 text-sm text-gray-300">对手：<strong className="text-white">{opponentName}</strong></p>
                    <p className="mt-1 text-sm text-gray-500">正在进入牌桌…</p>
                  </div>
                )}
              </div>
            )}

            {playMode === "bot" && (
              <div className="flex flex-col gap-4">
                <div>
                  <h2 className="font-bold text-white">单人测试</h2>
                  <p className="mt-1 text-sm leading-5 text-gray-500">与机器人对战，适合测试卡组与单卡效果。</p>
                </div>
                <div>
                  <p className="mb-2 text-sm text-gray-400">选择顺序</p>
                  <div className="grid grid-cols-2 rounded-xl border border-sky-800 p-1">
                    <button type="button" onClick={() => setBotGoFirst(true)} aria-pressed={botGoFirst} className={`min-h-11 rounded-lg text-sm font-bold ${botGoFirst ? "bg-sky-600 text-white" : "text-gray-400"}`}>先手</button>
                    <button type="button" onClick={() => setBotGoFirst(false)} aria-pressed={!botGoFirst} className={`min-h-11 rounded-lg text-sm font-bold ${!botGoFirst ? "bg-sky-600 text-white" : "text-gray-400"}`}>后手</button>
                  </div>
                </div>
                <button type="button" onClick={handleBotMatch} disabled={!canEnter} className="h-12 w-full rounded-xl bg-sky-600 text-base font-bold text-white hover:bg-sky-500 disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-600">
                  开始单人测试
                </button>
              </div>
            )}

            {playMode === "friend" && (
              <div className="flex flex-col gap-4">
                {roomMode === "none" && !roomCode && (
                  <>
                    <div>
                      <h2 className="font-bold text-white">好友房</h2>
                      <p className="mt-1 text-sm leading-5 text-gray-500">创建房间码，或输入好友发来的房间码。</p>
                    </div>
                    <div className="grid grid-cols-2 gap-3">
                      <button type="button" onClick={handleCreateRoom} disabled={!canEnter} className="min-h-12 rounded-xl bg-blue-600 px-3 text-sm font-bold text-white hover:bg-blue-500 disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-600">创建房间</button>
                      <button type="button" onClick={handleJoinRoom} disabled={!canEnter} className="min-h-12 rounded-xl bg-green-600 px-3 text-sm font-bold text-white hover:bg-green-500 disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-600">加入房间</button>
                    </div>
                  </>
                )}

                {!roomCode && roomMode === "create" && roomOperation === "creating" && (
                  <div className="flex flex-col items-center gap-3 py-2" role="status">
                    <div className="h-6 w-6 animate-spin rounded-full border-2 border-blue-500 border-t-transparent" />
                    <p className="font-bold text-blue-300">正在创建房间…</p>
                    <button type="button" onClick={handleCancelRoom} className="min-h-11 rounded-lg px-4 text-sm text-gray-400 hover:bg-gray-800 hover:text-white">取消</button>
                  </div>
                )}

                {roomCode && roomMode === "create" && (
                  <div className="flex flex-col items-center gap-3 py-1" role="status">
                    <p className="text-sm text-gray-400">把房间码发给好友</p>
                    <p className="select-all font-mono text-3xl font-black tracking-[0.24em] text-blue-300">{roomCode}</p>
                    <div className="grid w-full grid-cols-2 gap-3">
                      <button type="button" onClick={copyRoomCode} className="min-h-11 rounded-xl bg-blue-600 px-3 text-sm font-bold text-white hover:bg-blue-500">{copied ? "已复制" : "复制房间码"}</button>
                      <button type="button" onClick={handleCancelRoom} className="min-h-11 rounded-xl bg-gray-800 px-3 text-sm text-gray-300 hover:bg-gray-700">取消房间</button>
                    </div>
                    <p className="text-sm text-gray-500">等待对手加入…</p>
                  </div>
                )}

                {roomMode === "join" && !roomCode && (
                  <div className="flex flex-col gap-3">
                    <label htmlFor="join-room-code" className="text-sm font-medium text-gray-300">房间码</label>
                    <input
                      id="join-room-code"
                      value={joinInput}
                      onChange={(e) => setJoinInput(e.target.value.toUpperCase())}
                      placeholder="输入 6 位房间码"
                      maxLength={6}
                      autoCapitalize="characters"
                      className="h-12 w-full rounded-xl border border-gray-700 bg-gray-800 px-3 text-center font-mono text-lg tracking-[0.2em] text-white outline-none focus:border-green-500"
                      onKeyDown={(e) => e.key === "Enter" && confirmJoinRoom()}
                    />
                    <div className="grid grid-cols-2 gap-3">
                      <button type="button" onClick={handleCancelRoom} className="min-h-11 rounded-xl bg-gray-800 px-3 text-sm text-gray-300 hover:bg-gray-700">取消</button>
                      <button
                        type="button"
                        onClick={confirmJoinRoom}
                        disabled={joinInput.trim().length < 6 || roomOperation === "joining" || connState !== "connected"}
                        className="min-h-11 rounded-xl bg-green-600 px-3 text-sm font-bold text-white hover:bg-green-500 disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-600"
                      >
                        {roomOperation === "joining" ? "正在加入…" : "加入房间"}
                      </button>
                    </div>
                  </div>
                )}
              </div>
            )}
          </section>

          <SpectateSettingsPanel locked={modeLocked} />

          {entryHint && matchState === "idle" && roomOperation === "idle" && (
            <p className="pb-2 text-center text-sm text-gray-500">{entryHint}</p>
          )}

          <aside
            aria-label="平台声明"
            className="rounded-xl border border-gray-800 bg-gray-950/50 px-3 py-2.5 text-center text-xs leading-5 text-gray-500 @[640px]:px-4"
          >
            平台声明：本平台仅供技术学习与交流使用，不提供商品、服务或任何商业交易，亦不参与或支持任何形式的商业行为。
          </aside>
        </div>
      </div>

      <Modal
        open={Boolean(pendingFaction)}
        onClose={() => setPendingFaction(null)}
        title="确认更换排位阵营"
        mobileSheet
        maxWidthClass="max-w-md"
      >
        <p className="text-sm leading-6 text-gray-300">确认改为{pendingFaction ? RANK_FACTION_NAMES[pendingFaction] : "新阵营"}吗？此操作会清空本赛季的悬赏金、定级进度和战绩，且无法恢复。</p>
        <div className="mt-5 grid grid-cols-2 gap-3">
          <button type="button" onClick={() => setPendingFaction(null)} className="min-h-11 rounded-xl bg-gray-800 px-4 text-sm font-bold text-gray-200 hover:bg-gray-700">取消</button>
          <button type="button" onClick={confirmFactionChange} className="min-h-11 rounded-xl bg-violet-600 px-4 text-sm font-black text-white hover:bg-violet-500">确认更换并清空</button>
        </div>
      </Modal>

      <div className="hidden w-72 shrink-0 border-l border-gray-800 @[1024px]:block">
        <ChatPanel />
      </div>
    </div>
  );
}
