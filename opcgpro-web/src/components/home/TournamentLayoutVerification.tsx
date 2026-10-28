"use client";

import { useEffect, useState } from "react";
import TournamentPanel from "./TournamentPanel";
import LayoutPreviewFrame from "./LayoutPreviewFrame";
import type { TournamentDetail } from "@/types/tournament";
import { useNetStore } from "@/store/netStore";

function sample(manual: boolean, registration?: "checkin" | "checkedin" | "host", bye = false): TournamentDetail {
  const now = Date.now();
  const entrants = Array.from({ length: registration === "host" || bye ? 7 : 8 }, (_, i) => ({ account: `p${i}`, name: `赛事选手${i + 1}`, status: registration ? "registered" : "active", checkedIn: registration ? registration !== "checkin" : true, leader: "OP15-001", deckRaw: null }));
  return {
    id: "layout-tournament", revision: 3, name: "GrandUMI 社区周末挑战赛", description: "欢迎参加社区赛事。每桌两名玩家落座即可开始，下一轮由赛事管理员控制。",
    owner: "manager", ownerName: "社区举办者", status: registration ? "approved" : "swiss", capacity: 8, format: "standard", registrationOpensAt: now - 3_600_000, registrationClosesAt: registration ? now + 6_600_000 : now - 600_000, startsAt: registration ? now + 7_200_000 : now - 300_000,
    rewards: "冠军纪念荣誉", announcement: "本轮结算后休息5分钟，下一轮请等待管理员公告。", reviewReason: "", swissRounds: 3, cutSize: 4, managers: ["manager"], judges: ["judge"], rulesetId: "builtin", champion: null,
    canManage: !registration, canJudge: !registration, canReview: !registration, canEdit: false,
    canHost: registration === "host" || !registration, startedEntrantCount: registration ? 0 : bye ? 7 : 8, reputation: 85,
    reputationHistory: [{ tournamentId: "earlier", delta: -15, score: 85, reason: "签到后退赛", at: now - 86_400_000 }],
    mine: registration && registration !== "host" ? { account: "p0", status: "registered", checkedIn: registration === "checkedin", deckName: "测试卡组", deckRaw: "" } : null, entrants,
    contacts: [...entrants.map((e, i) => ({ account: e.account, name: e.name, qq: i === 7 ? null : `100000000${i}` })), { account: "manager", name: "社区举办者", qq: "2000000000" }, { account: "judge", name: "赛事裁判", qq: "3000000000" }],
    rounds: registration ? [] : [{ number: 1, stage: "swiss", status: manual ? "manual" : "live", publishedAt: now - 240_000, endReason: manual ? "管理员结束本轮" : "", tables: Array.from({ length: 4 }, (_, i) => ({
      id: `table-${i}`, number: i + 1, player0: `p${i * 2}`, player1: bye && i === 3 ? null : `p${i * 2 + 1}`, seated0: i < 2, seated1: i === 0,
      status: bye && i === 3 ? "finished" : manual ? "manual" : i === 0 ? "playing" : "waiting", roomId: i === 0 ? "test-room" : null,
      startedAt: i === 0 ? now - 180_000 : null, extraTurnLimit: null, outcome: bye && i === 3 ? "bye" : null, originalOutcome: manual && i === 0 ? "p0" : null, reason: bye && i === 3 ? "人数为奇数，本轮轮空" : "", confirmed: bye && i === 3, confirmedBy: null, confirmedAt: null,
    })) }], standings: entrants.map((e, i) => ({ account: e.account, name: e.name, points: 0, wins: 0, losses: 0, byes: 0, omw: .33, oomw: .33, rank: i + 1, status: "active", placement: null })), appeals: [], audit: [],
  };
}

export default function TournamentLayoutVerification({ manual, registration, bye, timing }: { manual: boolean; registration?: "checkin" | "checkedin" | "host"; bye?: boolean; timing?: "overdue" | "final" }) {
  const [ready, setReady] = useState(false);
  const [state] = useState(() => {
    const value = sample(manual, registration, bye);
    if (timing) {
      const round = value.rounds[0]; const table = round.tables[0];
      table.startedAt = Date.now() - 7_200_000;
      if (timing === "final") { round.stage = "cut"; round.number = 5; round.tables = [table]; value.status = "cut"; }
      else table.timeoutNotifiedAt = Date.now() - 1_000;
    }
    return value;
  });
  useEffect(() => { useNetStore.setState({ account: registration && registration !== "host" ? "p0" : "manager", loggedIn: true, connState: "connected" }); setReady(true); }, [registration]);
  return <LayoutPreviewFrame mode="desktop"><main className="h-full overflow-hidden" data-tournament-layout-verification data-tournament-layout-ready={ready}><TournamentPanel preview={state} /></main></LayoutPreviewFrame>;
}
