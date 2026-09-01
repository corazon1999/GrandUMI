import assert from "node:assert/strict";
import test from "node:test";
import { detectCardZoneTransitions } from "../src/lib/cardZoneTransitions.ts";

function player(overrides = {}) {
  return {
    handCardNumbers: [],
    handCount: 0,
    fieldCards: [],
    stageNumber: null,
    stageId: null,
    trashNumbers: [],
    deckCount: 40,
    lifeCount: 5,
    lifeFaceUp: Array.from({ length: 5 }, () => ({ faceUp: false, number: null })),
    ...overrides,
  };
}

function detect(previous, current, context = {}) {
  return detectCardZoneTransitions(
    { my: previous, opponent: null },
    { my: current, opponent: null },
    context,
  );
}

function route(transition) {
  return `${transition.from}->${transition.to}`;
}

test("识别卡组到手牌", () => {
  const transitions = detect(
    player({ handCardNumbers: ["A"], handCount: 1 }),
    player({ handCardNumbers: ["A", "B"], handCount: 2, deckCount: 39 }),
  );
  assert.deepEqual(transitions.map(route), ["deck->hand"]);
  assert.equal(transitions[0].cardNumber, "B");
});

test("识别手牌到生命", () => {
  const transitions = detect(
    player({ handCardNumbers: ["A", "B"], handCount: 2 }),
    player({ handCardNumbers: ["B"], handCount: 1, lifeCount: 6 }),
  );
  assert.deepEqual(transitions.map(route), ["hand->life"]);
  assert.equal(transitions[0].cardNumber, "A");
  assert.equal(transitions[0].toFaceUp, false);
});

test("识别卡组到生命", () => {
  const transitions = detect(player(), player({ deckCount: 39, lifeCount: 6 }));
  assert.deepEqual(transitions.map(route), ["deck->life"]);
  assert.equal(transitions[0].cardNumber, undefined);
});

test("识别手牌和卡组到角色区", () => {
  const handToField = detect(
    player({ handCardNumbers: ["A"], handCount: 1 }),
    player({ fieldCards: [{ id: "field-a", number: "A" }] }),
  );
  assert.deepEqual(handToField.map(route), ["hand->field"]);

  const deckToField = detect(
    player(),
    player({ deckCount: 39, fieldCards: [{ id: "field-b", number: "B" }] }),
  );
  assert.deepEqual(deckToField.map(route), ["deck->field"]);
});

test("海克斯双舞台分别识别登场与废弃移动", () => {
  const addSecondStage = detect(
    player({
      handCardNumbers: ["STAGE-B"],
      handCount: 1,
      stages: [{ id: "stage-a", number: "STAGE-A" }],
    }),
    player({
      stages: [
        { id: "stage-a", number: "STAGE-A" },
        { id: "stage-b", number: "STAGE-B" },
      ],
    }),
    { lastAction: "PlayCard", actionPayload: { cardId: "stage-b", cardNumber: "STAGE-B" } },
  );
  assert.deepEqual(addSecondStage.map(route), ["hand->stage"]);
  assert.equal(addSecondStage[0].targetCardId, "stage-b");

  const removeOneStage = detect(
    player({
      stages: [
        { id: "stage-a", number: "STAGE-A", tapped: true },
        { id: "stage-b", number: "STAGE-B" },
      ],
    }),
    player({
      stages: [{ id: "stage-b", number: "STAGE-B" }],
      trashNumbers: ["STAGE-A"],
    }),
  );
  assert.deepEqual(removeOneStage.map(route), ["stage->trash"]);
  assert.equal(removeOneStage[0].sourceCardId, "stage-a");
  assert.equal(removeOneStage[0].fromRotation, 90);
});

test("识别场上到墓地和生命到手牌", () => {
  const fieldToTrash = detect(
    player({ fieldCards: [{ id: "field-a", number: "A", isTapped: true }] }),
    player({ trashNumbers: ["A"] }),
  );
  assert.deepEqual(fieldToTrash.map(route), ["field->trash"]);
  assert.equal(fieldToTrash[0].fromRotation, 90);

  const lifeToHand = detect(
    player(),
    player({ handCardNumbers: ["C"], handCount: 1, lifeCount: 4 }),
  );
  assert.deepEqual(lifeToHand.map(route), ["life->hand"]);
  assert.equal(lifeToHand[0].cardNumber, "C");
});

test("同一快照抽一张再出一张时拆成两段移动", () => {
  const transitions = detect(
    player({ handCardNumbers: ["A"], handCount: 1 }),
    player({
      handCardNumbers: ["B"],
      handCount: 1,
      deckCount: 39,
      fieldCards: [{ id: "field-a", number: "A" }],
    }),
    { lastAction: "PlayCard", actionPayload: { cardId: "field-a", cardNumber: "A" } },
  );
  assert.deepEqual(transitions.map(route).sort(), ["deck->hand", "hand->field"]);
  assert.equal(transitions.find((item) => route(item) === "hand->field")?.cardNumber, "A");
  assert.equal(transitions.find((item) => route(item) === "deck->hand")?.cardNumber, "B");
});

test("同名牌导致手牌净状态不变时仍按出牌动作拆分", () => {
  const transitions = detect(
    player({ handCardNumbers: ["A"], handCount: 1 }),
    player({
      handCardNumbers: ["A"],
      handCount: 1,
      deckCount: 39,
      fieldCards: [{ id: "field-a", number: "A" }],
    }),
    { lastAction: "PlayCard", actionPayload: { cardId: "field-a", cardNumber: "A" } },
  );
  assert.deepEqual(transitions.map(route).sort(), ["deck->hand", "hand->field"]);
});

test("隐藏的对手手牌只产生卡背移动，不附带卡号", () => {
  const transitions = detectCardZoneTransitions(
    { my: null, opponent: player({ handCount: 1 }) },
    { my: null, opponent: player({ handCount: 2, deckCount: 39 }) },
  );
  assert.deepEqual(transitions.map(route), ["deck->hand"]);
  assert.equal(transitions[0].cardNumber, undefined);
  assert.equal(transitions[0].toFaceUp, false);
});
