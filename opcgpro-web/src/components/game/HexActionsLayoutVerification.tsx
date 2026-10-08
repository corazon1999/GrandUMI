"use client";

import { useEffect, useState } from "react";
import GameBoard from "@/components/game/GameBoard";
import LayoutPreviewFrame from "@/components/home/LayoutPreviewFrame";
import GameOverOverlay from "@/components/game/GameOverOverlay";
import { useGameStore } from "@/store/gameStore";
import { useSettingsStore } from "@/store/settingsStore";
import type { MsgGameState, PlayerSnapshot } from "@/types/net";

const CHARACTER_ID = "layout-hex-slaughterhouse-character";

function player(own: boolean): PlayerSnapshot {
  return {
    name: own ? "屠宰场布局验证" : "布局验证对手",
    cardBackId: "classic",
    spriteMap: {},
    handCardIds: [],
    handCardNumbers: [],
    handCardCosts: [],
    handCardCounters: [],
    handCount: 0,
    fieldCards: own
      ? [{
          id: CHARACTER_ID,
          number: "OP15-003",
          isTapped: false,
          powerCurrent: 4_000,
          cost: 3,
          attachedDon: 2,
          canDetachAllDon: true,
          gainedKeywords: [],
          effectsNullified: false,
          cannotActivateNextReset: false,
          cannotBeRested: false,
          activatedUsedThisTurn: false,
          oncePerTurnEffectAvailable: false,
          turnPlayed: 2,
          canAttack: true,
          cannotAttack: false,
          canActivateEffect: false,
        }]
      : [],
    stageNumber: null,
    stageId: null,
    stageTapped: false,
    stages: [],
    trashNumbers: [],
    deckCount: 40,
    lifeCount: 4,
    lifeNumbers: [],
    lifeFaceUp: [],
    leaderId: own ? "layout-hex-leader-self" : "layout-hex-leader-opponent",
    leaderNumber: own ? "OP15-001" : "OP15-002",
    leaderTapped: false,
    leaderPower: 5_000,
    leaderAttachedDon: 0,
    leaderGainedKeywords: [],
    leaderCanAttack: true,
    leaderCannotAttack: false,
    leaderEnterEffectNullified: false,
    leaderCanActivateEffect: false,
    stageCanActivateEffect: false,
    leaderActivatedUsedThisTurn: false,
    stageActivatedUsedThisTurn: false,
    leaderOncePerTurnEffectAvailable: false,
    stageOncePerTurnEffectAvailable: false,
    costActive: own ? 1 : 0,
    costRest: own ? 2 : 0,
    costAttached: own ? 2 : 0,
    costNextResetInactive: own ? 2 : 0,
    donDeckCount: own ? 5 : 10,
    hasReDraw: false,
    mulliganDone: true,
  };
}

function snapshot(): MsgGameState {
  return {
    proto: "MsgGameState",
    tick: 1,
    my: player(true),
    opponent: player(false),
    phase: "Main",
    currentTurn: true,
    turnCount: 3,
    firstPlayer: 0,
    firstPlayerChosen: true,
    openingStage: "Playing",
    isFirstPlayer: true,
    canChooseFirstPlayer: false,
    diceWinnerIsMe: true,
    startingDiceRolls: [],
    mulliganBothDone: true,
    matchKind: "Hex",
    isGameOver: false,
    winnerIsMe: false,
    gameOverReason: "",
    viewerKind: "player",
    lastAction: "HexLayoutVerification",
    actionPayload: "{}",
    pendingPrompt: null,
    battle: null,
  };
}

export default function HexActionsLayoutVerification({ hunter = false, showHunterResult = false }: { hunter?: boolean; showHunterResult?: boolean }) {
  const [ready, setReady] = useState(false);

  useEffect(() => {
    const previousAnimationSpeed = useSettingsStore.getState().animationSpeed;
    useSettingsStore.setState({ animationSpeed: "off" });
    const store = useGameStore.getState();
    store.resetGame();
    store.setMode("Player");
    const state = snapshot();
    if (hunter) {
      state.matchKind = "Ranked";
      state.my.rankIdentity = { faction: "east", tier: "十人斩", division: null, placementGames: 5, placementRequired: 0, seasonTitles: ["S1 海贼王"] };
      state.opponent.rankIdentity = { faction: "north", tier: "百人斩", division: null, placementGames: 5, placementRequired: 0, seasonTitles: ["S1 海军元帅"] };
    }
    store.syncFromServer(state);
    if (showHunterResult) useGameStore.setState(s => ({ isGameOver: true, winnerIsMe: true,
      cinematic: { ...s.cinematic, settlementReady: true } }));
    useGameStore.getState().setSelectedField(CHARACTER_ID);
    setReady(true);
    return () => {
      useGameStore.getState().resetGame();
      useSettingsStore.setState({ animationSpeed: previousAnimationSpeed });
    };
  }, [hunter, showHunterResult]);

  return (
    <LayoutPreviewFrame mode="mobile-landscape" rotateQuarterTurn edgeToEdge>
      <main data-hex-actions-layout-verification className="h-full w-full overflow-hidden bg-[#07111f]">
        {ready && <GameBoard isObserver={false} isPlayback={false} />}
        {ready && showHunterResult && <GameOverOverlay isObserver={false} onReturnToHome={() => {}} />}
      </main>
    </LayoutPreviewFrame>
  );
}
