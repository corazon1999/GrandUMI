"use client";

import { useEffect, useState } from "react";
import { useNetStore } from "@/store/netStore";
import { HomeRequest } from "@/net/HomeProtocol";
import { loadAllDecks, getSpriteMap, subscribeDecksUpdated, type SavedDeck } from "@/data/DeckMapper";

export default function FriendlyRoomPanel() {
  const room    = useNetStore((s) => s.friendlyRoom);
  const account = useNetStore((s) => s.account);
  const connState = useNetStore((s) => s.connState);
  const [decks, setDecks]     = useState<Record<string, SavedDeck>>({});
  const [picking, setPicking] = useState(false);
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    const refresh = () => setDecks(loadAllDecks());
    refresh();
    return subscribeDecksUpdated(refresh);
  }, []);

  if (!room) return null;

  const myIndex  = room.players.findIndex((p) => p.account === account);
  const oppIndex = myIndex === 0 ? 1 : 0;
  const me  = room.players[myIndex];
  const opp = room.players[oppIndex];
  const myScore  = room.scores[myIndex] ?? 0;
  const oppScore = room.scores[oppIndex] ?? 0;

  const selectDeck = (name: string) => {
    const saved = loadAllDecks()[name];
    if (!saved) return;
    const cards = [saved.leader, ...saved.cards].join("\n");
    if (typeof window !== "undefined") {
      sessionStorage.setItem("grandumi_spriteMap", JSON.stringify(getSpriteMap(name)));
    }
    HomeRequest.friendlySelectDeck(cards, name);
    setPicking(false);
  };

  const toggleReady = () => HomeRequest.friendlyReady(!me?.ready);
  const leave = () => HomeRequest.friendlyLeave();
  const canReady = !!me?.deckName && !!opp && room.state === "lobby" && connState === "connected";

  const copyRoomCode = () => {
    if (!room.roomCode) return;
    navigator.clipboard.writeText(room.roomCode).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }).catch(() => {});
  };

  const deckEntries = Object.entries(decks);

  return (
    <main
      className="flex h-[100dvh] flex-col items-center gap-5 overflow-y-auto bg-gray-950 px-4 py-5 sm:justify-center sm:p-8"
      style={{
        paddingTop: "calc(1.25rem + env(safe-area-inset-top))",
        paddingBottom: "calc(1.25rem + env(safe-area-inset-bottom))",
      }}
    >
      <h1 className="text-center text-xl font-bold text-white sm:text-2xl">
        {room.origin === "roomCode" ? "房间码友谊战" : "友谊战房间"}
      </h1>

      {room.roomCode && (
        <div className="flex w-full max-w-md items-center justify-between gap-3 rounded-2xl border border-blue-800 bg-blue-950/30 px-4 py-3">
          <div className="min-w-0">
            <p className="text-sm text-gray-500">等待对手加入，房间码</p>
            <p className="select-all truncate font-mono text-2xl font-black tracking-widest text-blue-400">{room.roomCode}</p>
          </div>
          <button
            type="button"
            onClick={copyRoomCode}
            className="min-h-11 shrink-0 rounded-xl bg-blue-600 px-4 text-sm font-bold text-white hover:bg-blue-500"
          >
            {copied ? "已复制" : "复制"}
          </button>
        </div>
      )}

      {connState !== "connected" && (
        <p role="status" className="w-full max-w-md rounded-xl border border-yellow-700/60 bg-yellow-950/30 px-4 py-3 text-sm leading-5 text-yellow-300">
          {connState === "recovering" ? "连接已恢复，正在同步房间状态…" : "连接中断，房间将保留 30 秒等待重连…"}
        </p>
      )}

      {/* 比分 */}
      <div className="flex w-full max-w-md items-center justify-center gap-3">
        <span className="min-w-0 flex-1 truncate text-right text-sm text-gray-400">{me?.name ?? "我"}</span>
        <span className="text-3xl font-black text-orange-400">{myScore} : {oppScore}</span>
        <span className="min-w-0 flex-1 truncate text-sm text-gray-400">{opp?.name ?? "对方"}</span>
      </div>

      {/* 双方状态卡 */}
      <div className="grid w-full max-w-md grid-cols-1 gap-3 sm:grid-cols-2">
        <PlayerCard title="我"   name={me?.name}  deckName={me?.deckName}  ready={me?.ready} connected={me?.connected} mine />
        <PlayerCard title="对手" name={opp?.name} deckName={opp?.deckName} ready={opp?.ready} connected={opp?.connected} waiting={!opp} />
      </div>

      {/* 操作区 */}
      <div className="flex w-full max-w-md flex-col items-center gap-3">
        <button
          type="button"
          onClick={() => setPicking((v) => !v)}
          disabled={room.state !== "lobby" || connState !== "connected"}
          className="min-h-12 w-full rounded-xl border border-gray-700 bg-gray-800 px-3 text-sm text-white transition-colors hover:border-orange-500 disabled:text-gray-600"
        >
          {me?.deckName ? `已选：${me.deckName}（点击更换）` : "选择卡组"}
        </button>

        {picking && (
          <div className="flex max-h-52 w-full flex-col gap-1 overflow-y-auto rounded-xl border border-gray-800 bg-gray-900 p-2">
            {deckEntries.length === 0 ? (
              <p className="text-gray-600 text-xs text-center py-3">还没有卡组，去「卡组」面板创建</p>
            ) : (
              deckEntries.map(([name, d]) => (
                <button
                  key={name}
                  onClick={() => selectDeck(name)}
                  className="flex min-h-12 items-center gap-2 rounded-lg px-2 py-1.5 text-left hover:bg-gray-800"
                >
                  {/* eslint-disable-next-line @next/next/no-img-element */}
                  <img
                    src={d.leaderSprite || "/sprites/CardBack.png"}
                    alt={d.leaderName}
                    className="w-7 h-10 object-cover rounded border border-gray-700 shrink-0"
                    onError={(e) => { (e.target as HTMLImageElement).src = "/sprites/CardBack.png"; }}
                  />
                  <div className="min-w-0">
                    <p className="text-white text-xs truncate">{name}</p>
                    <p className="text-gray-500 text-[10px] truncate">{d.leaderName}</p>
                  </div>
                </button>
              ))
            )}
          </div>
        )}

        <button
          type="button"
          onClick={toggleReady}
          disabled={!canReady}
          className={`min-h-12 w-full rounded-xl text-sm font-bold transition-all ${
            !canReady
              ? "bg-gray-800 text-gray-600 cursor-not-allowed"
              : me?.ready
                ? "bg-green-600 hover:bg-green-500 text-white"
                : "bg-orange-500 hover:bg-orange-400 text-white"
          }`}
        >
          {!opp ? "等待对手加入" : room.state === "starting" ? "正在创建对局…" : me?.ready ? "✓ 已准备（点击取消）" : "准备"}
        </button>

        {me?.ready && opp?.ready && (
          <p className="text-green-400 text-xs">双方已准备，即将开始对战…</p>
        )}

        <button
          type="button"
          onClick={leave}
          className="mt-1 min-h-11 rounded-lg px-4 text-sm text-gray-500 transition-colors hover:bg-gray-900 hover:text-red-400"
        >
          退出房间
        </button>
      </div>
    </main>
  );
}

function PlayerCard({ title, name, deckName, ready, connected, mine, waiting }: {
  title: string;
  name?: string;
  deckName?: string | null;
  ready?: boolean;
  connected?: boolean;
  mine?: boolean;
  waiting?: boolean;
}) {
  return (
    <div className={`flex w-full flex-col items-center gap-2 rounded-xl border-2 p-4 ${
      mine ? "border-orange-500/60 bg-orange-500/5" : "border-gray-700 bg-gray-900"
    }`}>
      <span className="text-sm text-gray-500">{title}</span>
      <p className="text-white font-bold text-sm truncate w-full text-center">{waiting ? "等待加入…" : (name ?? "?")}</p>
      <p className="text-gray-400 text-xs truncate w-full text-center">{waiting ? "房间码已开放" : (deckName ?? "未选卡组")}</p>
      <span className={`text-sm font-bold ${ready ? "text-green-400" : "text-gray-600"}`}>
        {waiting ? "等待中" : connected === false ? "重连中" : ready ? "已准备" : "未准备"}
      </span>
    </div>
  );
}
