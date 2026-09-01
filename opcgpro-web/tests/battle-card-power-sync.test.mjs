import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { getBattleCardPowerBonus } from "../src/lib/battleCardPower.ts";

const baseBattle = {
  attackerPlayer: 0,
  attackerCardId: "attacker",
  targetIsLeader: true,
  targetCardId: null,
  blockerCardId: null,
  attackerBonus: 3000,
  defenderBonus: 2000,
};

test("战斗卡面力量：攻击者同步本次战斗加成，非参与者不受影响", () => {
  assert.equal(getBattleCardPowerBonus(baseBattle, "attacker", "leader"), 3000);
  assert.equal(getBattleCardPowerBonus(baseBattle, "attacker", "character"), 3000);
  assert.equal(getBattleCardPowerBonus(baseBattle, "unrelated", "character"), 0);
  assert.equal(getBattleCardPowerBonus(null, "attacker", "leader"), 0);
});

test("战斗卡面力量：未阻挡时仅当前领航或角色目标同步防守加成", () => {
  assert.equal(getBattleCardPowerBonus(baseBattle, "defender-leader", "leader"), 2000);
  assert.equal(getBattleCardPowerBonus(baseBattle, "defender-character", "character"), 0);

  const characterBattle = {
    ...baseBattle,
    targetIsLeader: false,
    targetCardId: "defender-character",
  };
  assert.equal(getBattleCardPowerBonus(characterBattle, "defender-leader", "leader"), 0);
  assert.equal(getBattleCardPowerBonus(characterBattle, "defender-character", "character"), 2000);
});

test("战斗卡面力量：阻挡替换后防守加成跟随阻挡者而不是原目标", () => {
  const blockedBattle = {
    ...baseBattle,
    blockerCardId: "blocker",
  };

  assert.equal(getBattleCardPowerBonus(blockedBattle, "defender-leader", "leader"), 0);
  assert.equal(getBattleCardPowerBonus(blockedBattle, "blocker", "character"), 2000);
  assert.equal(getBattleCardPowerBonus(blockedBattle, "original-target", "character"), 0);
});

test("战斗卡面力量：旧回放缺少 bonus 字段时安全回退为零", () => {
  const legacyBattle = {
    attackerPlayer: 0,
    attackerCardId: "attacker",
    targetIsLeader: true,
    targetCardId: null,
    blockerCardId: null,
  };

  assert.equal(getBattleCardPowerBonus(legacyBattle, "attacker", "leader"), 0);
  assert.equal(getBattleCardPowerBonus(legacyBattle, "defender", "leader"), 0);
});

test("领航与角色卡面都把战斗加成并入既有 powerBuff，且不重复计算咚", async () => {
  const [leaderSource, fieldSource] = await Promise.all([
    readFile(new URL("../src/components/game/LeaderCard.tsx", import.meta.url), "utf8"),
    readFile(new URL("../src/components/game/FieldArea.tsx", import.meta.url), "utf8"),
  ]);

  assert.match(leaderSource, /getBattleCardPowerBonus\(battle, player\.leaderId, "leader"\)/);
  assert.match(
    leaderSource,
    /powerBuff=\{player\.leaderPower \+ battlePowerBonus - \(leader\.power \?\? 0\) - player\.leaderAttachedDon \* 1000\}/,
  );
  assert.match(fieldSource, /getBattleCardPowerBonus\(battle, fc\.id, "character"\)/);
  assert.match(
    fieldSource,
    /powerBuff=\{fc\.powerCurrent \+ battlePowerBonus - \(cardData\?\.power \?\? 0\) - attachedCount \* 1000\}/,
  );
});
