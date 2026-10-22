export interface TournamentSummary {
  id: string; revision: number; name: string; owner: string; ownerName: string;
  status: string; capacity: number; format: string; startsAt: number;
  registrationOpensAt: number; registrationClosesAt: number;
  registered: number; mine: boolean; canManage: boolean;
  ownerQq?: string | null;
}
export interface TournamentContact { account: string; name: string; qq: string | null }
export interface TournamentTable {
  id: string; number: number; player0: string | null; player1: string | null;
  seated0: boolean; seated1: boolean; status: string; roomId: string | null;
  startedAt: number | null; extraTurnLimit: number | null; outcome: string | null;
  originalOutcome: string | null; reason: string; confirmed: boolean;
  confirmedBy: string | null; confirmedAt: number | null;
}
export interface TournamentRound {
  number: number; stage: string; status: string; publishedAt: number; endReason: string;
  tables: TournamentTable[];
}
export interface TournamentStanding {
  account: string; name: string; points: number; wins: number; losses: number;
  byes: number; omw: number; oomw: number; rank: number; status: string; placement: number | null;
}
export interface TournamentDetail {
  id: string; revision: number; name: string; description: string; owner: string;
  ownerName: string; status: string; capacity: number; format: string;
  registrationOpensAt: number; registrationClosesAt: number; startsAt: number;
  rewards: string; announcement: string; reviewReason: string;
  swissRounds: number; cutSize: number; managers: string[]; judges: string[];
  startedEntrantCount?: number; actualStartedAt?: number | null; canHost?: boolean;
  reputation?: number; reputationHistory?: { tournamentId: string; delta: number; score: number; reason: string; at: number }[];
  rulesetId: string; champion: string | null; canManage: boolean; canJudge: boolean;
  canReview: boolean; canEdit: boolean;
  contacts?: TournamentContact[];
  mine: { account: string; status: string; checkedIn: boolean; deckName: string; deckRaw: string; leaveReason?: string } | null;
  entrants: { account: string; name: string; status: string; checkedIn: boolean; leader: string; deckRaw: string | null; leaveReason?: string; leftAt?: number | null }[];
  standings: TournamentStanding[]; rounds: TournamentRound[];
  appeals: { id: string; tableId: string; account: string; description: string; status: string; resolution: string; at: number }[];
  audit: { at: number; actor: string; action: string; detail: string }[];
}
export interface TournamentResponse {
  proto: "MsgTournament"; requestId: string; result: boolean; logStr?: string;
  items?: TournamentSummary[]; canReview?: boolean; tournament?: TournamentDetail;
  confirmationToken?: string | null; serverNow?: number;
}
export interface TournamentClock {
  proto: "MsgTournamentClock"; tournamentId: string; name: string; round: number;
  stage: string; table: number; startedAt: number; deadline: number;
  extraTurnLimit: number | null; manual: boolean; serverNow: number;
  roomId?: string | null; contacts?: TournamentContact[];
}
