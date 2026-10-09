"use client";

import { useEffect, useState } from "react";
import { useNetStore } from "@/store/netStore";
import type { RankProfileSnapshot, RankLeaderboardItem, FactionStanding, RankPlayerSettlement } from "@/types/net";
import HunterSeasonPanel from "./HunterSeasonPanel";
import LobbyPanel from "./LobbyPanel";
import ProfilePanel from "./ProfilePanel";
import LeaderLeaderboardPanel from "./LeaderLeaderboardPanel";
import { SeasonHonorBadge } from "@/components/ui/HunterIdentity";
import HexActionsLayoutVerification from "@/components/game/HexActionsLayoutVerification";
import { registerHomeProtocols } from "@/net/HomeProtocol";
import { eventBus } from "@/net/eventBus";
import type { MsgEquipSeasonTitle } from "@/types/net";

export const HUNTER_FIXTURE_RESULT: RankPlayerSettlement = {
  isHunterSeason: true, account: "hunter", faction: "east", tier: "十人斩", division: null,
  rankPointsBefore: 9, rankPointsAfter: 16, rankPointDelta: 7, baseRankPointDelta: 1,
  streakAdjustment: 3, winStreakEndedBounty: 2, endedWinStreak: 5, rankDifference: 0,
  rankDifferenceAdjustment: 1, rankProtectionAdjustment: 0, resultStreak: 5, won: true,
  rankPointFormulaApplied: true, placementGames: 5, placementRequired: 0, placementCompleted: false, winStreak: 5,
};

/** 仅供受环境变量保护的路由展示真实业务组件。 */
export default function HuntersLayoutVerification({ view }: { view: string }) {
  const [ready, setReady] = useState(false);
  useEffect(() => {
    registerHomeProtocols();
    const titles = ["S1 海贼王", "S1 四皇", "S1 海军元帅", "S1 海军大将", "S1 世界之王", "S1 五老星"];
    const profile: RankProfileSnapshot = { seasonId: "S2", seasonStartsAtUtc: "2026-10-08T00:00:00Z", seasonEndsAtUtc: "2026-11-30T16:00:00Z",
      faction: view === "choice" || view === "effects" ? null : "east", tier: "十人斩", division: null, rankPoints: 16, highestRankPoints: 16,
      placementGames: 5, placementRequired: 0, games: 8, wins: 6, losses: 2, seasonTitles: titles,
      equippedSeasonTitle: view === "profile" ? null : titles[0] };
    if (view === "lobby-zero") Object.assign(profile, { faction: "west", tier: "见习猎人", rankPoints: 0,
      highestRankPoints: 0, games: 0, wins: 0, losses: 0, equippedSeasonTitle: null });
    const seas = ["east", "west", "south", "north"] as const;
    const items: RankLeaderboardItem[] = titles.map((title, i) => ({ rank: i+1, factionRank: i+1, displayName: `四海猎人·${i+1}`,
      faction: seas[i%4], tier: "十人斩", division: null, rankPoints: 90-i*10, games: 50, wins: 30,
      winRate: 60, seasonTitles: titles, equippedSeasonTitle: title, isCurrentPlayer: i === 0 }));
    const standings: FactionStanding[] = seas.map((sea, i) => ({ faction: sea, rank: i+1, totalRankPoints: 500-i*60, playerCount: 30, games: 100, wins: 50 }));
    const store = useNetStore.getState();
    store.setRankSnapshot("standard", profile, items, standings, { snapshotVersion: 1, generatedAtUtc: new Date().toISOString() });
    store.setLastRankResult(HUNTER_FIXTURE_RESULT);
    useNetStore.setState({ matchQueueKind: "ranked", playerName: "四海猎人", connState: "connected", account: "hunter",
      selectedDeck: { name: "出海验证卡组", cards: "领航: OP01-001\n50 OP01-002", leader: "OP01-001", leaderName: "路飞", leaderSprite: "" } });
    setReady(true);
    // 连接替身仅用于布局验证，避免页面全局连接生命周期覆盖固定测试状态。
    return useNetStore.subscribe(state => {
      if (state.connState !== "connected") useNetStore.setState({ connState: "connected" });
    });
  }, [view]);
  if (!ready) return null;
  const equipFixture = (title: string | null) => {
    const requestId = `fixture-title-${Date.now()}`;
    setTimeout(() => {
      const state = useNetStore.getState();
      const profile = { ...state.rankProfiles.standard!, equippedSeasonTitle: title };
      const leaderboard = state.rankLeaderboards.standard.map(item => item.isCurrentPlayer ? { ...item, equippedSeasonTitle: title } : item);
      eventBus.emit("message", { proto: "MsgEquipSeasonTitle", requestId, title, result: true,
        profiles: { standard: profile, wild: profile },
        snapshots: ["standard", "wild"].map(mode => ({ mode, profile, leaderboard, factionStandings: [], snapshotVersion: Date.now(), generatedAtUtc: new Date().toISOString() })),
      } as MsgEquipSeasonTitle);
    }, 100);
    return requestId;
  };
  if (view === "game" || view === "win") return <HexActionsLayoutVerification hunter showHunterResult={view === "win"} />;
  return <main data-hunters-layout-verification className="@container h-dvh w-full overflow-y-auto bg-[#050811] text-white">
    {view === "choice" || view === "lobby" || view === "lobby-zero" ? <LobbyPanel onGoToDeck={() => {}} />
      : view === "rank" ? <LeaderLeaderboardPanel />
      : view === "profile" ? <ProfilePanel profileEditor={null} onOpenPlayers={() => {}} onOpenHistory={() => {}} onOpenChangelog={() => {}} onOpenSettings={() => {}} onOpenFeedback={() => {}} onEquipSeasonTitle={equipFixture} />
      : <div className="mx-auto max-w-4xl space-y-8 p-4 sm:p-8">
        <HunterSeasonPanel profile={useNetStore.getState().rankProfiles.standard!} mode="standard" />
        <section><h2 className="mb-4 text-lg font-bold">S1 · 称号典藏</h2><div className="grid gap-4 sm:grid-cols-3">{["海贼王","四皇","海军元帅","海军大将","世界之王","五老星"].map(title => <SeasonHonorBadge key={title} title={`S1 ${title}`} />)}</div></section>
      </div>}
  </main>;
}
