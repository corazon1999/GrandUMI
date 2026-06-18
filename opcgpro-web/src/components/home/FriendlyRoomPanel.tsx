"use client";

import { useEffect, useState } from "react";
import { useNetStore } from "@/store/netStore";
import { HomeRequest } from "@/net/HomeProtocol";
import { loadAllDecks, getSpriteMap, type SavedDeck } from "@/data/DeckMapper";

export default function FriendlyRoomPanel() {
  const room    = useNetStore((s) => s.friendlyRoom);
  const account = useNetStore((s) => s.account);
  const [decks, setDecks]     = useState<Record<string, SavedDeck>>({});
  const [picking, setPicking] = useState(false);

  useEffect(() => { setDecks(loadAllDecks()); }, []);

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

  const deckEntries = Object.entries(decks);

  return (
    <div className="flex h-screen flex-col items-center justify-center bg-gray-950 p-8 gap-6">
      <h2 className="text-white font-bold text-2xl">友谊战房间</h2>

      {/* 比分 */}
      <div className="flex items-center gap-4">
        <span className="text-sm text-gray-400 truncate max-w-[8rem] text-right">{me?.name ?? "我"}</span>
        <span className="text-3xl font-black text-orange-400">{myScore} : {oppScore}</span>
        <span className="text-sm text-gray-400 truncate max-w-[8rem]">{opp?.name ?? "对方"}</span>
      </div>

      {/* 双方状态卡 */}
      <div className="flex gap-6">
        <PlayerCard title="我"   name={me?.name}  deckName={me?.deckName}  ready={me?.ready}  mine />
        <PlayerCard title="对手" name={opp?.name} deckName={opp?.deckName} ready={opp?.ready} />
      </div>

      {/* 操作区 */}
      <div className="flex flex-col items-center gap-3 w-full max-w-sm">
        <button
          onClick={() => setPicking((v) => !v)}
          className="w-full py-2 rounded-lg bg-gray-800 border border-gray-700 text-white text-sm hover:border-orange-500 transition-colors"
        >
          {me?.deckName ? `已选：${me.deckName}（点击更换）` : "选择卡组"}
        </button>

        {picking && (
          <div className="w-full max-h-48 overflow-y-auto flex flex-col gap-1 bg-gray-900 border border-gray-800 rounded-lg p-2">
            {deckEntries.length === 0 ? (
              <p className="text-gray-600 text-xs text-center py-3">还没有卡组，去「卡组」面板创建</p>
            ) : (
              deckEntries.map(([name, d]) => (
                <button
                  key={name}
                  onClick={() => selectDeck(name)}
                  className="flex items-center gap-2 px-2 py-1.5 rounded hover:bg-gray-800 text-left"
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
          onClick={toggleReady}
          disabled={!me?.deckName}
          className={`w-full py-2.5 rounded-xl font-bold text-sm transition-all ${
            !me?.deckName
              ? "bg-gray-800 text-gray-600 cursor-not-allowed"
              : me?.ready
                ? "bg-green-600 hover:bg-green-500 text-white"
                : "bg-orange-500 hover:bg-orange-400 text-white"
          }`}
        >
          {me?.ready ? "✓ 已准备（点击取消）" : "准备"}
        </button>

        {me?.ready && opp?.ready && (
          <p className="text-green-400 text-xs">双方已准备，即将开始对战…</p>
        )}

        <button
          onClick={leave}
          className="text-gray-500 hover:text-red-400 text-xs transition-colors mt-2"
        >
          退出房间
        </button>
      </div>
    </div>
  );
}

function PlayerCard({ title, name, deckName, ready, mine }: {
  title: string; name?: string; deckName?: string | null; ready?: boolean; mine?: boolean;
}) {
  return (
    <div className={`w-44 rounded-xl border-2 p-4 flex flex-col items-center gap-2 ${
      mine ? "border-orange-500/60 bg-orange-500/5" : "border-gray-700 bg-gray-900"
    }`}>
      <span className="text-gray-500 text-[10px]">{title}</span>
      <p className="text-white font-bold text-sm truncate w-full text-center">{name ?? "?"}</p>
      <p className="text-gray-400 text-xs truncate w-full text-center">{deckName ?? "未选卡组"}</p>
      <span className={`text-[11px] font-bold ${ready ? "text-green-400" : "text-gray-600"}`}>
        {ready ? "已准备" : "未准备"}
      </span>
    </div>
  );
}
