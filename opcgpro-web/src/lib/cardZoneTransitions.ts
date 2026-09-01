export type CardZone = "deck" | "hand" | "life" | "field" | "stage" | "trash";
export type CardZoneSide = "my" | "opponent";

export interface ZoneFieldCard {
  id: string;
  number: string;
  isTapped?: boolean;
}

export interface ZonePlayerSnapshot {
  handCardNumbers: string[];
  handCount: number;
  fieldCards: ZoneFieldCard[];
  stageNumber: string | null;
  stageId: string | null;
  stageTapped?: boolean;
  stages?: Array<{ id: string; number: string; tapped?: boolean }>;
  trashNumbers: string[];
  deckCount: number;
  lifeCount: number;
  lifeFaceUp?: { faceUp: boolean; number: string | null }[];
}

function stageCards(player: ZonePlayerSnapshot) {
  if (player.stages?.length) return player.stages;
  return player.stageId && player.stageNumber
    ? [{ id: player.stageId, number: player.stageNumber, tapped: player.stageTapped }]
    : [];
}

export interface ZoneTransitionContext {
  lastAction?: string;
  actionPayload?: Record<string, unknown> | null;
}

export interface CardZoneTransition {
  side: CardZoneSide;
  from: CardZone;
  to: CardZone;
  cardNumber?: string;
  sourceCardId?: string;
  targetCardId?: string;
  sourceIndex?: number;
  targetIndex?: number;
  fromFaceUp: boolean;
  toFaceUp: boolean;
  fromRotation: number;
  toRotation: number;
}

interface ZoneToken {
  zone: CardZone;
  number?: string;
  cardId?: string;
  index?: number;
  faceUp: boolean;
  rotation: number;
  forcedPlayId?: string;
}

interface IndexedCard {
  number: string;
  index: number;
}

const ZONE_PAIR_COST: Record<CardZone, Partial<Record<CardZone, number>>> = {
  deck: { hand: 1, life: 2, field: 2, stage: 2, trash: 2 },
  hand: { field: 1, stage: 1, life: 1, trash: 1, deck: 2 },
  life: { hand: 1, field: 1, stage: 1, trash: 1, deck: 2 },
  field: { trash: 1, hand: 1, life: 1, deck: 2 },
  stage: { trash: 1, hand: 1, life: 1, deck: 2 },
  trash: { field: 1, stage: 1, hand: 1, life: 2, deck: 2 },
};

function multisetDelta(previous: readonly string[], current: readonly string[]) {
  const currentRemaining = new Map<string, number>();
  for (const number of current) {
    currentRemaining.set(number, (currentRemaining.get(number) ?? 0) + 1);
  }

  const removed: IndexedCard[] = [];
  previous.forEach((number, index) => {
    const remaining = currentRemaining.get(number) ?? 0;
    if (remaining > 0) currentRemaining.set(number, remaining - 1);
    else removed.push({ number, index });
  });

  const previousRemaining = new Map<string, number>();
  for (const number of previous) {
    previousRemaining.set(number, (previousRemaining.get(number) ?? 0) + 1);
  }

  const added: IndexedCard[] = [];
  current.forEach((number, index) => {
    const remaining = previousRemaining.get(number) ?? 0;
    if (remaining > 0) previousRemaining.set(number, remaining - 1);
    else added.push({ number, index });
  });

  return { removed, added };
}

function visibleLifeNumbers(player: ZonePlayerSnapshot) {
  return (player.lifeFaceUp ?? [])
    .filter((card) => card.faceUp && card.number)
    .map((card) => card.number as string);
}

function countTokens(zone: CardZone, count: number, faceUp: boolean): ZoneToken[] {
  return Array.from({ length: Math.max(0, count) }, () => ({
    zone,
    faceUp,
    rotation: 0,
  }));
}

function buildCountZoneTokens(
  zone: "deck" | "life",
  previousCount: number,
  currentCount: number,
  previousKnown: readonly string[] = [],
  currentKnown: readonly string[] = [],
) {
  const delta = currentCount - previousCount;
  const outgoing = countTokens(zone, -delta, false);
  const incoming = countTokens(zone, delta, false);

  if (zone === "life") {
    const known = multisetDelta(previousKnown, currentKnown);
    known.removed.slice(0, outgoing.length).forEach((card, index) => {
      outgoing[index].number = card.number;
      outgoing[index].faceUp = true;
    });
    known.added.slice(0, incoming.length).forEach((card, index) => {
      incoming[index].number = card.number;
      incoming[index].faceUp = true;
    });
  }

  return { outgoing, incoming };
}

function addedPublicCardId(
  previous: ZonePlayerSnapshot,
  current: ZonePlayerSnapshot,
  cardId: string,
) {
  const previousFieldIds = new Set(previous.fieldCards.map((card) => card.id));
  const field = current.fieldCards.find((card) => card.id === cardId && !previousFieldIds.has(card.id));
  if (field) return true;
  const previousStageIds = new Set(stageCards(previous).map((stage) => stage.id));
  return stageCards(current).some((stage) => stage.id === cardId && !previousStageIds.has(stage.id));
}

function forcedPlayForSide(
  previous: ZonePlayerSnapshot,
  current: ZonePlayerSnapshot,
  context: ZoneTransitionContext,
) {
  if (context.lastAction !== "PlayCard") return null;
  const cardId = typeof context.actionPayload?.cardId === "string" ? context.actionPayload.cardId : "";
  const cardNumber = typeof context.actionPayload?.cardNumber === "string"
    ? context.actionPayload.cardNumber
    : "";
  if (!cardId || !addedPublicCardId(previous, current, cardId)) return null;
  return { cardId, cardNumber };
}

function buildHandTokens(
  previous: ZonePlayerSnapshot,
  current: ZonePlayerSnapshot,
  forcedPlay: { cardId: string; cardNumber: string } | null,
) {
  const previousVisible = previous.handCardNumbers.length === previous.handCount;
  const currentVisible = current.handCardNumbers.length === current.handCount;
  const fullyVisible = previousVisible && currentVisible;
  const delta = current.handCount - previous.handCount;
  const known = fullyVisible
    ? multisetDelta(previous.handCardNumbers, current.handCardNumbers)
    : { removed: [] as IndexedCard[], added: [] as IndexedCard[] };

  const outgoing: ZoneToken[] = known.removed.map((card) => ({
    zone: "hand",
    number: card.number,
    index: card.index,
    faceUp: true,
    rotation: 0,
  }));
  const incoming: ZoneToken[] = known.added.map((card) => ({
    zone: "hand",
    number: card.number,
    index: card.index,
    faceUp: true,
    rotation: 0,
  }));

  if (!fullyVisible) {
    outgoing.push(...countTokens("hand", -delta, false).map((token, index) => ({
      ...token,
      index: Math.max(0, previous.handCount - 1 - index),
    })));
    incoming.push(...countTokens("hand", delta, false).map((token, index, tokens) => ({
      ...token,
      index: Math.max(0, current.handCount - tokens.length + index),
    })));
  }

  if (forcedPlay) {
    let forced = outgoing.find((token) =>
      !token.forcedPlayId && (!forcedPlay.cardNumber || token.number === forcedPlay.cardNumber));
    if (!forced) {
      const usedIndices = new Set(outgoing.map((token) => token.index).filter((index) => index != null));
      const sourceIndex = fullyVisible
        ? previous.handCardNumbers.findIndex((number, index) =>
            !usedIndices.has(index) && (!forcedPlay.cardNumber || number === forcedPlay.cardNumber))
        : -1;
      forced = {
        zone: "hand",
        number: forcedPlay.cardNumber || undefined,
        index: sourceIndex >= 0 ? sourceIndex : Math.max(0, previous.handCount - 1),
        faceUp: fullyVisible,
        rotation: 0,
      };
      outgoing.push(forced);
    }
    forced.forcedPlayId = forcedPlay.cardId;

    // 同一快照若“抽一张再打出一张”导致手牌净数量不变，需要补回被多重集抵消的入手 token。
    const requiredIncoming = Math.max(0, outgoing.length + delta);
    const usedTargetIndices = new Set(incoming.map((token) => token.index).filter((index) => index != null));
    while (incoming.length < requiredIncoming) {
      const targetIndex = fullyVisible
        ? current.handCardNumbers.findIndex((_, index) => !usedTargetIndices.has(index))
        : -1;
      if (targetIndex >= 0) usedTargetIndices.add(targetIndex);
      incoming.push({
        zone: "hand",
        number: targetIndex >= 0 ? current.handCardNumbers[targetIndex] : undefined,
        index: targetIndex >= 0 ? targetIndex : Math.max(0, current.handCount - 1),
        faceUp: fullyVisible,
        rotation: 0,
      });
    }
  }

  return { outgoing, incoming };
}

function buildFieldTokens(previous: ZonePlayerSnapshot, current: ZonePlayerSnapshot) {
  const previousById = new Map(previous.fieldCards.map((card) => [card.id, card]));
  const currentById = new Map(current.fieldCards.map((card) => [card.id, card]));
  const outgoing = previous.fieldCards
    .filter((card) => !currentById.has(card.id))
    .map<ZoneToken>((card) => ({
      zone: "field",
      number: card.number,
      cardId: card.id,
      faceUp: true,
      rotation: card.isTapped ? 90 : 0,
    }));
  const incoming = current.fieldCards
    .filter((card) => !previousById.has(card.id))
    .map<ZoneToken>((card) => ({
      zone: "field",
      number: card.number,
      cardId: card.id,
      faceUp: true,
      rotation: card.isTapped ? 90 : 0,
    }));

  return { outgoing, incoming };
}

function buildStageTokens(previous: ZonePlayerSnapshot, current: ZonePlayerSnapshot) {
  const previousStages = stageCards(previous);
  const currentStages = stageCards(current);
  const previousIds = new Set(previousStages.map((stage) => stage.id));
  const currentIds = new Set(currentStages.map((stage) => stage.id));
  const outgoing = previousStages
    .filter((stage) => !currentIds.has(stage.id))
    .map<ZoneToken>((stage) => ({
      zone: "stage",
      number: stage.number,
      cardId: stage.id,
      faceUp: true,
      rotation: stage.tapped ? 90 : 0,
    }));
  const incoming = currentStages
    .filter((stage) => !previousIds.has(stage.id))
    .map<ZoneToken>((stage) => ({
      zone: "stage",
      number: stage.number,
      cardId: stage.id,
      faceUp: true,
      rotation: stage.tapped ? 90 : 0,
    }));
  return { outgoing, incoming };
}

function buildTrashTokens(previous: ZonePlayerSnapshot, current: ZonePlayerSnapshot) {
  const delta = multisetDelta(previous.trashNumbers, current.trashNumbers);
  return {
    outgoing: delta.removed.map<ZoneToken>((card) => ({
      zone: "trash",
      number: card.number,
      index: card.index,
      faceUp: true,
      rotation: 0,
    })),
    incoming: delta.added.map<ZoneToken>((card) => ({
      zone: "trash",
      number: card.number,
      index: card.index,
      faceUp: true,
      rotation: 0,
    })),
  };
}

function pairScore(source: ZoneToken, target: ZoneToken) {
  if (source.zone === target.zone) return Number.POSITIVE_INFINITY;
  if (source.forcedPlayId && source.forcedPlayId === target.cardId) return -10_000;
  if (source.cardId && target.cardId && source.cardId === target.cardId) return -5_000;
  if (source.number && target.number && source.number === target.number) return -1_000;
  if (source.number && target.number && source.number !== target.number) return 100;
  return ZONE_PAIR_COST[source.zone][target.zone] ?? 20;
}

function detectPlayerTransitions(
  previous: ZonePlayerSnapshot,
  current: ZonePlayerSnapshot,
  side: CardZoneSide,
  context: ZoneTransitionContext,
) {
  const forcedPlay = forcedPlayForSide(previous, current, context);
  const hand = buildHandTokens(previous, current, forcedPlay);
  const field = buildFieldTokens(previous, current);
  const stage = buildStageTokens(previous, current);
  const trash = buildTrashTokens(previous, current);
  const deck = buildCountZoneTokens("deck", previous.deckCount, current.deckCount);
  const life = buildCountZoneTokens(
    "life",
    previous.lifeCount,
    current.lifeCount,
    visibleLifeNumbers(previous),
    visibleLifeNumbers(current),
  );
  const outgoing = [
    ...hand.outgoing,
    ...field.outgoing,
    ...stage.outgoing,
    ...trash.outgoing,
    ...deck.outgoing,
    ...life.outgoing,
  ];
  const incoming = [
    ...hand.incoming,
    ...field.incoming,
    ...stage.incoming,
    ...trash.incoming,
    ...deck.incoming,
    ...life.incoming,
  ];

  const transitions: CardZoneTransition[] = [];
  while (outgoing.length > 0 && incoming.length > 0) {
    let bestSource = -1;
    let bestTarget = -1;
    let bestScore = Number.POSITIVE_INFINITY;
    outgoing.forEach((source, sourceIndex) => {
      incoming.forEach((target, targetIndex) => {
        const score = pairScore(source, target);
        if (score < bestScore) {
          bestScore = score;
          bestSource = sourceIndex;
          bestTarget = targetIndex;
        }
      });
    });
    if (bestSource < 0 || bestTarget < 0 || !Number.isFinite(bestScore)) break;

    const [source] = outgoing.splice(bestSource, 1);
    const [target] = incoming.splice(bestTarget, 1);
    transitions.push({
      side,
      from: source.zone,
      to: target.zone,
      cardNumber: source.number ?? target.number,
      sourceCardId: source.cardId,
      targetCardId: target.cardId,
      sourceIndex: source.index,
      targetIndex: target.index,
      fromFaceUp: source.faceUp && !!(source.number ?? target.number),
      toFaceUp: target.faceUp && !!(source.number ?? target.number),
      fromRotation: source.rotation,
      toRotation: target.rotation,
    });
  }

  return transitions;
}

export function detectCardZoneTransitions(
  previous: { my: ZonePlayerSnapshot | null; opponent: ZonePlayerSnapshot | null },
  current: { my: ZonePlayerSnapshot | null; opponent: ZonePlayerSnapshot | null },
  context: ZoneTransitionContext = {},
) {
  const transitions: CardZoneTransition[] = [];
  if (previous.my && current.my) {
    transitions.push(...detectPlayerTransitions(previous.my, current.my, "my", context));
  }
  if (previous.opponent && current.opponent) {
    transitions.push(...detectPlayerTransitions(previous.opponent, current.opponent, "opponent", context));
  }
  return transitions;
}
