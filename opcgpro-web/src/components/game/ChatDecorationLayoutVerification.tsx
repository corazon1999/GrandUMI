"use client";

import { useEffect, useState } from "react";
import GameBoard from "@/components/game/GameBoard";
import GameOverOverlay from "@/components/game/GameOverOverlay";
import {
  GameCinematicController,
} from "@/components/game/GameCinematicLayer";
import { ChatDecorationExchangePanel } from "@/components/home/MainPanel";
import LayoutPreviewFrame from "@/components/home/LayoutPreviewFrame";
import { useGameStore } from "@/store/gameStore";
import { useNetStore, type ChatDecorationItem } from "@/store/netStore";
import type {
  GameCinematicPhraseEvent,
  MsgGameState,
  PlayerSnapshot,
} from "@/types/net";

function player(name: string, leaderId: string, leaderNumber: string): PlayerSnapshot {
  return {
    name,
    cardBackId: "classic",
    spriteMap: {},
    handCardIds: [],
    handCardNumbers: [],
    handCardCosts: [],
    handCardCounters: [],
    handCount: 0,
    fieldCards: [],
    stageNumber: null,
    stageId: null,
    stageTapped: false,
    stages: [],
    trashNumbers: [],
    deckCount: 42,
    lifeCount: 4,
    lifeNumbers: [],
    lifeFaceUp: [],
    leaderId,
    leaderNumber,
    leaderTapped: false,
    leaderPower: 5_000,
    leaderAttachedDon: 1,
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
    costActive: 3,
    costRest: 1,
    costAttached: 1,
    donDeckCount: 5,
    hasReDraw: false,
    mulliganDone: true,
  };
}

function phrase(
  eventId: string,
  sourceSeat: 0 | 1,
  displaySide: "self" | "opponent",
  displayName: string,
  text: string,
): GameCinematicPhraseEvent {
  return {
    eventId,
    sourceSeat,
    displaySide,
    displayName,
    id: eventId,
    name: "布局验证语录",
    text,
    rarity: "legendary",
    styleToken: sourceSeat === 0 ? "emperor" : "tide",
  };
}

function gameSnapshot(terminal: boolean): MsgGameState {
  const ownVictory = phrase(
    "layout-cinematic:victory:0",
    0,
    "self",
    "布局验证船长",
    "唯有胜者才是正义！",
  );
  return {
    proto: "MsgGameState",
    tick: terminal ? 20 : 10,
    my: player("布局验证船长", "layout-leader-self", "OP15-001"),
    opponent: player("布局验证对手", "layout-leader-opponent", "OP15-002"),
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
    matchKind: "Casual",
    isGameOver: terminal,
    isDraw: false,
    winnerIsMe: terminal,
    gameOverReason: terminal ? "布局验证对手生命耗尽" : "",
    viewerKind: "player",
    lastAction: terminal ? "DuelOver" : "MulliganComplete",
    actionPayload: "{}",
    pendingPrompt: null,
    battle: null,
    cinematic: {
      matchId: "layout-cinematic",
      openingEvents: terminal
        ? []
        : [
            phrase("layout-cinematic:opening:0", 0, "self", "布局验证船长", "我是要成为海贼王的男人!"),
            phrase(
              "layout-cinematic:opening:1",
              1,
              "opponent",
              "布局验证对手",
              "原来外面的世界里真的存在像你这样强大的男人。",
            ),
          ],
      terminal: terminal
        ? {
            eventId: "layout-cinematic:terminal",
            winnerSeat: 0,
            loserSeat: 1,
            winnerSide: "self",
            loserSide: "opponent",
            reason: "布局验证对手生命耗尽",
            victory: ownVictory,
          }
        : null,
    },
  };
}

const exchangeItems: ChatDecorationItem[] = [
  {
    id: "quote-pirate-king-man",
    name: "海贼王宣言",
    text: "我是要成为海贼王的男人!",
    rarity: "legendary",
    styleToken: "emperor",
    priceBerries: 50_000_000,
    owned: false,
    availableForPurchase: true,
    equippedSlots: [],
  },
  {
    id: "greeting-straw-hat",
    name: "草帽式问候",
    text: "嘿！来场痛快的对决吧！",
    rarity: "common",
    styleToken: "sunset",
    priceBerries: 50_000_000,
    owned: true,
    availableForPurchase: false,
    equippedSlots: ["opening", "victory"],
  },
  {
    id: "quote-binks-laugh",
    name: "骷髅之歌",
    text: "哟嚯嚯嚯嚯嚯嚯嚯！",
    rarity: "epic",
    styleToken: "feast",
    priceBerries: 50_000_000,
    owned: false,
    availableForPurchase: true,
    equippedSlots: [],
  },
];

function GameFixture({ terminal }: { terminal: boolean }) {
  const [ready, setReady] = useState(false);
  useEffect(() => {
    useGameStore.getState().resetGame();
    useGameStore.getState().setMode("Player");
    useGameStore.getState().syncFromServer(gameSnapshot(terminal));
    setReady(true);
    return () => useGameStore.getState().resetGame();
  }, [terminal]);

  return (
    <LayoutPreviewFrame mode="mobile-landscape" rotateQuarterTurn edgeToEdge>
      <main data-chat-decoration-layout-verification={terminal ? "terminal" : "opening"} className="h-full w-full overflow-hidden bg-[#07111f]">
        {ready && (
          <>
            <GameBoard isObserver={false} isPlayback={false} />
            <GameCinematicController />
            <GameOverOverlay isObserver={false} onReturnToHome={() => undefined} />
          </>
        )}
      </main>
    </LayoutPreviewFrame>
  );
}

function ExchangeFixture({ purchased }: { purchased: boolean }) {
  const [ready, setReady] = useState(false);
  useEffect(() => {
    useNetStore.setState((state) => ({
      connState: "disconnected",
      chatDecorationExchange: {
        ...state.chatDecorationExchange,
        snapshot: {
          walletMode: "season_peak_bounty",
          walletRule: "额度来自本赛季标准排位历史最高悬赏金；仅刷新纪录时补发新增差额，购买不影响排位，狂野排位不计入。",
          seasonId: "layout-season",
          balanceBerries: purchased ? 50_000_000 : 100_000_000,
          items: exchangeItems.map((item) => item.id === "quote-binks-laugh"
            ? { ...item, owned: purchased }
            : item),
        },
        pendingRequestId: null,
        pendingAction: null,
        error: null,
      },
    }));
    setReady(true);
  }, [purchased]);
  return (
    <LayoutPreviewFrame mode="mobile-portrait">
      <main data-chat-decoration-layout-verification={purchased ? "exchange-after" : "exchange-before"} className="h-full min-h-0 overflow-hidden bg-gray-950">
        {ready && <ChatDecorationExchangePanel />}
      </main>
    </LayoutPreviewFrame>
  );
}

export default function ChatDecorationLayoutVerification({
  view,
}: {
  view: "exchange" | "exchange-before" | "exchange-after" | "opening" | "terminal";
}) {
  return view.startsWith("exchange")
    ? <ExchangeFixture purchased={view === "exchange-after"} />
    : <GameFixture terminal={view === "terminal"} />;
}
