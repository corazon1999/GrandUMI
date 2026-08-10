"use client";

import { useEffect, useState } from "react";
import { useNetStore } from "@/store/netStore";
import { HomeRequest } from "@/net/HomeProtocol";
import { showMessage } from "@/components/ui/MessageBox";
import ChatPanel from "./ChatPanel";
import { advanceImageFallback, CARD_BACK_SRC, thumbSrc } from "@/lib/sprite";

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
  const rankProfile = useNetStore((s) => s.rankProfile);
  const rankLeaderboard = useNetStore((s) => s.rankLeaderboard);
  const playerName    = useNetStore((s) => s.playerName);
  const roomCode      = useNetStore((s) => s.roomCode);
  const roomOperation = useNetStore((s) => s.roomOperation);
  const connState     = useNetStore((s) => s.connState);

  const [roomMode, setRoomMode] = useState<"none" | "create" | "join">("none");
  const [playMode, setPlayMode] = useState<"match" | "friend" | "bot">("match");
  const [joinInput, setJoinInput] = useState("");
  const [copied, setCopied] = useState(false);
  const [botGoFirst, setBotGoFirst] = useState(true);

  // 主卡组须恰好 50 张（后端 DeckValidator 强制，不满会被拒，bug #183）。
  // 这里前置拦截：未满 50 时置灰按钮并提示，避免「点了没反应」。
  // mainCount === 0 表示解析失败/未知，fail-open 不拦截，交由后端校验兜底。
  const mainCount     = selectedDeck ? countMainCards(selectedDeck.cards) : 0;
  const deckIncomplete = mainCount > 0 && mainCount !== 50;
  const canEnter      = !!selectedDeck && !deckIncomplete && connState === "connected" && roomOperation === "idle";

  useEffect(() => {
    if (roomMode === "create" && roomOperation === "idle" && !roomCode) {
      setRoomMode("none");
    }
  }, [roomCode, roomMode, roomOperation]);

  const handleMatch = () => {
    if (!selectedDeck) return;
    const sent = HomeRequest.enterMatch(selectedDeck.cards, selectedDeck.name, matchQueueKind);
    if (!sent) {
      showMessage("服务器未连接，请稍后重试", "error");
    }
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
      : connState !== "connected"
        ? "服务器连接恢复后即可开始"
        : "";

  return (
    <div className="flex h-full min-w-0">
      <div className="min-w-0 flex-1 overflow-y-auto px-4 py-5 @[640px]:px-6 @[1024px]:flex @[1024px]:flex-col @[1024px]:items-center @[1024px]:justify-center @[1024px]:px-8 @[1024px]:py-8">
        <div className="mx-auto flex w-full max-w-xl flex-col gap-4 @[1024px]:gap-5">
          <div>
            <h1 className="text-xl font-bold text-white @[1024px]:text-2xl">开始对战</h1>
            <p className="mt-1 text-sm text-gray-500">选择模式，准备好后即可进入牌桌。</p>
          </div>

          <button
            type="button"
            onClick={onGoToDeck}
            className="w-full rounded-2xl border border-gray-800 bg-gray-900 p-4 text-left transition-colors hover:border-orange-700 active:bg-gray-800"
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

          <section className="rounded-2xl border border-gray-800 bg-gray-900 p-4 @[640px]:p-5">
            {playMode === "match" && (
              <div className="flex flex-col gap-3">
                {matchState === "idle" && (
                  <>
                    <div>
                      <h2 className="font-bold text-white">公开匹配</h2>
                      <p className="mt-1 text-sm leading-5 text-gray-500">排位计入段位积分，休闲不影响排名。</p>
                    </div>
                    <div className="grid grid-cols-2 rounded-xl border border-gray-800 bg-gray-950 p-1" aria-label="公开匹配类型">
                      <button
                        type="button"
                        onClick={() => useNetStore.getState().setMatchQueueKind("ranked")}
                        aria-pressed={matchQueueKind === "ranked"}
                        className={`min-h-11 rounded-lg px-3 text-sm font-black transition-colors ${matchQueueKind === "ranked" ? "bg-violet-600 text-white" : "text-gray-500 hover:bg-gray-800 hover:text-gray-200"}`}
                      >
                        排位匹配
                      </button>
                      <button
                        type="button"
                        onClick={() => useNetStore.getState().setMatchQueueKind("casual")}
                        aria-pressed={matchQueueKind === "casual"}
                        className={`min-h-11 rounded-lg px-3 text-sm font-black transition-colors ${matchQueueKind === "casual" ? "bg-orange-500 text-white" : "text-gray-500 hover:bg-gray-800 hover:text-gray-200"}`}
                      >
                        休闲匹配
                      </button>
                    </div>

                    {matchQueueKind === "ranked" && rankProfile && (
                      <div className="rounded-xl border border-violet-800/70 bg-violet-950/25 p-3">
                        <div className="flex items-center justify-between gap-3">
                          <div>
                            <p className="text-xs font-bold text-violet-300">{rankProfile.seasonId} 当前段位</p>
                            <p className="mt-1 text-lg font-black text-white">
                              {rankProfile.placementGames < rankProfile.placementRequired
                                ? `定级中 ${rankProfile.placementGames}/${rankProfile.placementRequired}`
                                : `${rankProfile.tier}${rankProfile.division ? ` ${["", "I", "II", "III"][rankProfile.division]}` : ""}`}
                            </p>
                          </div>
                          {rankProfile.placementGames >= rankProfile.placementRequired && (
                            <div className="text-right">
                              <p className="text-2xl font-black text-violet-200">{rankProfile.rankPoints}</p>
                              <p className="text-[11px] text-gray-500">RP</p>
                            </div>
                          )}
                        </div>
                        <p className="mt-2 text-xs text-gray-500">战绩 {rankProfile.wins} 胜 / {rankProfile.losses} 负 · 赛季结束 {new Date(rankProfile.seasonEndsAtUtc).toLocaleDateString("zh-CN")}</p>
                        <details className="mt-2 border-t border-violet-900/60 pt-2">
                          <summary className="min-h-11 cursor-pointer py-2 text-sm font-bold text-violet-300">查看排位榜</summary>
                          <div className="max-h-44 space-y-1 overflow-y-auto pr-1">
                            {rankLeaderboard.length === 0 ? (
                              <p className="py-2 text-xs text-gray-500">本赛季暂时还没有完成定级的玩家。</p>
                            ) : rankLeaderboard.slice(0, 20).map((item) => (
                              <div key={`${item.rank}-${item.displayName}`} className="flex items-center gap-2 rounded-lg bg-black/20 px-2 py-1.5 text-xs">
                                <span className="w-6 text-center font-black text-violet-300">#{item.rank}</span>
                                <span className="min-w-0 flex-1 truncate text-gray-200">{item.displayName}</span>
                                <span className="text-gray-400">{item.tier}{item.division ? ` ${["", "I", "II", "III"][item.division]}` : ""}</span>
                                <span className="w-12 text-right font-bold text-white">{item.rankPoints}</span>
                              </div>
                            ))}
                          </div>
                        </details>
                      </div>
                    )}
                    <button
                      type="button"
                      onClick={handleMatch}
                      disabled={!canEnter}
                      className={`h-12 w-full rounded-xl text-base font-bold text-white transition-colors disabled:cursor-not-allowed disabled:bg-gray-800 disabled:text-gray-600 ${matchQueueKind === "ranked" ? "bg-violet-600 hover:bg-violet-500 active:bg-violet-700" : "bg-orange-500 hover:bg-orange-400 active:bg-orange-600"}`}
                    >
                      开始{matchQueueKind === "ranked" ? "排位" : "休闲"}匹配
                    </button>
                  </>
                )}
                {matchState === "matching" && (
                  <div className="flex flex-col items-center gap-3 py-2" role="status">
                    <div className="h-6 w-6 animate-spin rounded-full border-2 border-orange-500 border-t-transparent" />
                    <p className="font-bold text-orange-300">正在寻找{matchQueueKind === "ranked" ? "排位" : "休闲"}对手…</p>
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

          {entryHint && matchState === "idle" && roomOperation === "idle" && (
            <p className="pb-2 text-center text-sm text-gray-500">{entryHint}</p>
          )}
        </div>
      </div>

      <div className="hidden w-72 shrink-0 border-l border-gray-800 @[1024px]:block">
        <ChatPanel />
      </div>
    </div>
  );
}
