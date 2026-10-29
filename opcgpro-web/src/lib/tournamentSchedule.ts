import type { TournamentDetail } from "../types/tournament";

export const TOURNAMENT_CAPACITIES = [8, 16, 32, 64, 128, 256] as const;
export const TOURNAMENT_SWISS_ROUNDS = [3, 4, 5, 6, 7, 8, 9] as const;
export const TOURNAMENT_CUT_SIZES = [1, 2, 4, 8, 16, 32] as const;

export function recommendedTournamentSchedule(capacity: number) {
  return { rounds: Math.log2(capacity), cut: capacity <= 16 ? 4 : capacity <= 128 ? 8 : 16 };
}

export function tournamentStartSchedule(tournament: TournamentDetail, signed: number) {
  const count = Math.max(2, Math.min(tournament.capacity, signed));
  return {
    rounds: tournament.configuredSwissRounds ?? Math.ceil(Math.log2(count)),
    cut: Math.min(tournament.configuredCutSize ?? recommendedTournamentSchedule(tournament.capacity).cut, 2 ** Math.floor(Math.log2(count))),
  };
}

export function tournamentCutLabel(cut: number) {
  return cut === 1 ? "瑞士轮第1名直接夺冠" : `前${cut}名晋级淘汰赛`;
}
