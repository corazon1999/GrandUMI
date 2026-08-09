import assert from "node:assert/strict";
import test from "node:test";
import {
  nextLeaderLeaderboardSort,
  sortLeaderLeaderboardItems,
} from "../src/lib/leaderLeaderboardSort.ts";

const createItem = (leaderNumber, overrides = {}) => ({
  rank: 1,
  leaderNumber,
  games: 20,
  wins: 10,
  losses: 10,
  winRate: 0.5,
  usageRate: 0.1,
  firstGames: 10,
  firstWinRate: 0.5,
  secondGames: 10,
  secondWinRate: 0.5,
  insufficientSample: false,
  ...overrides,
});

test("统计列按默认、降序、升序、默认循环", () => {
  const descending = nextLeaderLeaderboardSort(null, "games");
  assert.deepEqual(descending, { key: "games", direction: "desc" });

  const ascending = nextLeaderLeaderboardSort(descending, "games");
  assert.deepEqual(ascending, { key: "games", direction: "asc" });

  assert.equal(nextLeaderLeaderboardSort(ascending, "games"), null);
  assert.deepEqual(nextLeaderLeaderboardSort(ascending, "winRate"), {
    key: "winRate",
    direction: "desc",
  });
});

test("统计列排序稳定且不改变服务端默认顺序", () => {
  const source = [
    createItem("OP01-001", { games: 30 }),
    createItem("OP01-002", { games: 10 }),
    createItem("OP01-003", { games: 30 }),
  ];

  assert.equal(sortLeaderLeaderboardItems(source, null), source);
  assert.deepEqual(
    sortLeaderLeaderboardItems(source, { key: "games", direction: "desc" }).map((item) => item.leaderNumber),
    ["OP01-001", "OP01-003", "OP01-002"],
  );
  assert.deepEqual(
    sortLeaderLeaderboardItems(source, { key: "games", direction: "asc" }).map((item) => item.leaderNumber),
    ["OP01-002", "OP01-001", "OP01-003"],
  );
  assert.deepEqual(source.map((item) => item.leaderNumber), ["OP01-001", "OP01-002", "OP01-003"]);
});

test("先后攻胜率没有数据时始终排在末尾", () => {
  const source = [
    createItem("OP01-001", { firstWinRate: null }),
    createItem("OP01-002", { firstWinRate: 0.4 }),
    createItem("OP01-003", { firstWinRate: 0.7 }),
  ];

  assert.deepEqual(
    sortLeaderLeaderboardItems(source, { key: "firstWinRate", direction: "desc" }).map((item) => item.leaderNumber),
    ["OP01-003", "OP01-002", "OP01-001"],
  );
  assert.deepEqual(
    sortLeaderLeaderboardItems(source, { key: "firstWinRate", direction: "asc" }).map((item) => item.leaderNumber),
    ["OP01-002", "OP01-003", "OP01-001"],
  );
});
