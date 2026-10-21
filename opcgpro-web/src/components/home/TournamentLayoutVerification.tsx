"use client";

import { useEffect, useState } from "react";
import TournamentPanel from "./TournamentPanel";
import LayoutPreviewFrame from "./LayoutPreviewFrame";
import type { TournamentDetail } from "@/types/tournament";
import { useNetStore } from "@/store/netStore";

function sample(manual: boolean): TournamentDetail {
  const now = Date.now();
  const entrants = Array.from({ length: 8 }, (_, i) => ({ account: `p${i}`, name: `赛事选手${i + 1}`, status: "active", checkedIn: true, leader: "OP15-001", deckRaw: null }));
  return {
    id: "layout-tournament", revision: 3, name: "GrandUMI 社区周末挑战赛", description: "欢迎参加社区赛事。每桌两名玩家落座即可开始，下一轮由赛事管理员控制。",
    owner: "manager", ownerName: "社区举办者", status: "swiss", capacity: 8, format: "standard", registrationOpensAt: now - 3_600_000, registrationClosesAt: now - 600_000, startsAt: now - 300_000,
    rewards: "冠军纪念荣誉", announcement: "本轮结算后休息5分钟，下一轮请等待管理员公告。", reviewReason: "", swissRounds: 3, cutSize: 4, managers: ["manager"], judges: ["judge"], rulesetId: "builtin", champion: null,
    canManage: true, canJudge: true, canReview: true, canEdit: false, mine: null, entrants,
    rounds: [{ number: 1, stage: "swiss", status: manual ? "manual" : "live", publishedAt: now - 240_000, endReason: manual ? "管理员结束本轮" : "", tables: Array.from({ length: 4 }, (_, i) => ({
      id: `table-${i}`, number: i + 1, player0: `p${i * 2}`, player1: `p${i * 2 + 1}`, seated0: i < 2, seated1: i === 0,
      status: manual ? "manual" : i === 0 ? "playing" : "waiting", roomId: i === 0 ? "test-room" : null,
      startedAt: i === 0 ? now - 180_000 : null, extraTurnLimit: null, outcome: null, originalOutcome: manual && i === 0 ? "p0" : null, reason: "", confirmed: false, confirmedBy: null, confirmedAt: null,
    })) }], standings: entrants.map((e, i) => ({ account: e.account, name: e.name, points: 0, wins: 0, losses: 0, byes: 0, omw: .33, oomw: .33, rank: i + 1, status: "active", placement: null })), appeals: [], audit: [],
  };
}

export default function TournamentLayoutVerification({ manual }: { manual: boolean }) {
  const [state] = useState(() => sample(manual));
  useEffect(() => { useNetStore.setState({ account: "manager", loggedIn: true, connState: "connected" }); }, []);
  return <LayoutPreviewFrame mode="desktop"><main className="h-full overflow-hidden" data-tournament-layout-verification><TournamentPanel preview={state} /></main></LayoutPreviewFrame>;
}
