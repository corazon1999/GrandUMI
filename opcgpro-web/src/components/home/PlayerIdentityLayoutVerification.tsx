"use client";

import { useEffect, useState } from "react";
import PlayerName from "@/components/ui/PlayerName";
import { LeaderChampionBadge } from "@/components/ui/LeaderChampionBadge";
import { SeaNameBadge } from "@/components/ui/HunterIdentity";
import { usePlayerIdentityStore } from "@/store/playerIdentityStore";
import { useNetStore } from "@/store/netStore";
import { registerHomeProtocols } from "@/net/HomeProtocol";
import { eventBus } from "@/net/eventBus";
import type { MsgPublicPlayerIdentities } from "@/types/net";
import PlayerListPanel from "./PlayerListPanel";
import FriendsPanel from "./FriendsPanel";
import ChatPanel from "./ChatPanel";
import HexActionsLayoutVerification from "@/components/game/HexActionsLayoutVerification";

const names = ["释迦·三十二字昵称与双称号兼容验证玩家", "对手·长昵称与海军元帅称号兼容验证"];
const titles = ["S1 海贼王", "S1 海军元帅"];

/** 环境保护的页面使用真实业务组件和协议处理器验证称号展示。 */
export default function PlayerIdentityLayoutVerification({ view }: { view: string }) {
  const [ready, setReady] = useState(false);
  useEffect(() => {
    registerHomeProtocols();
    usePlayerIdentityStore.getState().merge(names.map((name, i) => ({ name, equippedSeasonTitle: titles[i] })));
    useNetStore.setState({ playerName: names[0], account: "fixture-owner", connState: "connected", loggedIn: false,
      playerList: names.map((name, i) => ({ name, account: `fixture-${i}`, status: "idle", championLeaderNumbers: ["OP01-001"] })),
      friends: names.map((name, i) => ({ name, account: `fixture-${i}`, avatar: "", online: true, status: "idle", friendsSince: 1, championLeaderNumbers: ["OP01-001"] })),
      chatMessages: names.map((Name, i) => ({ Name, Msg: `称号与聊天正文正常分行展示 ${i + 1}`, type: 0, time: 1 })),
    });
    setReady(true);
  }, []);
  if (!ready) return null;
  if (view === "game" || view === "intro") return <HexActionsLayoutVerification publicTitles intro={view === "intro"} desktop={window.innerWidth >= 600} />;
  const update = (equippedSeasonTitle: string | null) => eventBus.emit("message", {
    proto: "MsgPublicPlayerIdentities", identities: [{ name: names[0], equippedSeasonTitle }],
  } as MsgPublicPlayerIdentities);
  return <main data-player-identities-verification className="@container flex h-dvh w-full flex-col overflow-y-auto bg-[#050811] p-3 text-white">
    {view === "players" ? <PlayerListPanel open onClose={() => {}} />
      : view === "friends" ? <FriendsPanel open onClose={() => {}} />
      : view === "chat" ? <ChatPanel />
      : <>
        <div className="mb-4 flex flex-wrap gap-2">
          <button data-title-fixture-change onClick={() => update("S1 五老星")} className="min-h-11 min-w-11 rounded bg-slate-700 p-3">切换称号</button>
          <button data-title-fixture-clear onClick={() => update(null)} className="min-h-11 min-w-11 rounded bg-slate-700 p-3">取消佩戴</button>
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          {names.flatMap((name, i) => [0, 1].map(copy => <section key={`${i}-${copy}`} data-nameplate-card className="min-w-0 rounded-xl border border-slate-700 p-3">
            <div className="flex min-w-0 flex-wrap items-start gap-2"><PlayerName name={name} /><LeaderChampionBadge leaderNumber="OP01-001" /></div>
            <p className="mt-2 text-xs"><SeaNameBadge sea="east" /> · 十人斩</p>
            <p className="mt-2 text-sm">此处正文不能与昵称、赛季称号或冠军称号重叠。</p>
          </section>))}
        </div>
      </>}
  </main>;
}
