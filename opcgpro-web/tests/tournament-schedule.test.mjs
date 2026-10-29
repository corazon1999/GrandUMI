import assert from "node:assert/strict";
import test from "node:test";
import { recommendedTournamentSchedule, tournamentStartSchedule, tournamentCutLabel, TOURNAMENT_CAPACITIES, TOURNAMENT_SWISS_ROUNDS, TOURNAMENT_CUT_SIZES } from "../src/lib/tournamentSchedule.ts";

test("人数档位保持不变，每档提供原有推荐值以及全部可选轮次和出轮档位", () => {
  assert.deepEqual(TOURNAMENT_CAPACITIES, [8, 16, 32, 64, 128, 256]);
  assert.deepEqual(TOURNAMENT_SWISS_ROUNDS, [3, 4, 5, 6, 7, 8, 9]);
  assert.deepEqual(TOURNAMENT_CUT_SIZES, [1, 2, 4, 8, 16, 32]);
  assert.deepEqual(TOURNAMENT_CAPACITIES.map(recommendedTournamentSchedule), [
    { rounds: 3, cut: 4 }, { rounds: 4, cut: 4 }, { rounds: 5, cut: 8 },
    { rounds: 6, cut: 8 }, { rounds: 7, cut: 8 }, { rounds: 8, cut: 16 },
  ]);
});

test("未满员预览保留自定义轮次，晋级名额依实际人数缩减但保留单人夺冠", () => {
  const tournament = { capacity: 64, configuredSwissRounds: 9, configuredCutSize: 32 };
  assert.deepEqual(tournamentStartSchedule(tournament, 12), { rounds: 9, cut: 8 });
  assert.deepEqual(tournamentStartSchedule(tournament, 2), { rounds: 9, cut: 2 });
  assert.deepEqual(tournamentStartSchedule(tournament, 64), { rounds: 9, cut: 32 });
  assert.deepEqual(tournamentStartSchedule({ ...tournament, configuredCutSize: 1 }, 12), { rounds: 9, cut: 1 });
  assert.equal(tournamentCutLabel(1), "瑞士轮第1名直接夺冠");
  assert.equal(tournamentCutLabel(32), "前32名晋级淘汰赛");
});

test("候补签到不超过赛事容量，旧赛事未配置时沿用原实际人数赛程", () => {
  assert.deepEqual(tournamentStartSchedule({ capacity: 8, configuredSwissRounds: 7, configuredCutSize: 8 }, 12), { rounds: 7, cut: 8 });
  assert.deepEqual(tournamentStartSchedule({ capacity: 8 }, 3), { rounds: 2, cut: 2 });
  assert.deepEqual(tournamentStartSchedule({ capacity: 32, configuredSwissRounds: null, configuredCutSize: null }, 7), { rounds: 3, cut: 4 });
});
