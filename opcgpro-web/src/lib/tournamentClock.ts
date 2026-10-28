import type { TournamentRound, TournamentTable } from "@/types/tournament";

export function isTournamentFinal(round: TournamentRound) {
  return round.stage === "cut" && round.tables.length === 1;
}

export function tableTimeLimit(round: TournamentRound, table: TournamentTable) {
  return table.timeLimitMs ?? (isTournamentFinal(round) ? 0 : 1_800_000);
}

export function tableDeadline(round: TournamentRound, table: TournamentTable): number | null {
  const limit = tableTimeLimit(round, table);
  return limit === 0 || table.startedAt === null ? null : table.deadlineAt ?? table.startedAt + limit;
}

export function tableOverdue(round: TournamentRound, table: TournamentTable, now: number) {
  if (table.confirmed || round.status !== "live") return false;
  const deadline = tableDeadline(round, table);
  return table.status === "playing" && deadline !== null && now >= deadline
    || table.status === "blocked" && table.timeoutNotifiedAt != null;
}

export function tableClockLabel(round: TournamentRound, table: TournamentTable, now: number) {
  if (!table.player1) return "本轮轮空";
  if (table.confirmed) return "计时已结束";
  if (tableOverdue(round, table, now)) return "已超时，等待管理员处理";
  if (tableTimeLimit(round, table) === 0) return isTournamentFinal(round) ? "决赛不限时" : "不限时";
  const deadline = tableDeadline(round, table);
  if (deadline === null) return "双方落座后独立计时";
  const seconds = Math.max(0, Math.ceil((deadline - now) / 1000));
  return `本桌剩余 ${Math.floor(seconds / 60).toString().padStart(2, "0")}:${(seconds % 60).toString().padStart(2, "0")}`;
}
