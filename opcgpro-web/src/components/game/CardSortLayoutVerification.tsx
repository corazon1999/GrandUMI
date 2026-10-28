"use client";

import { useCallback, useEffect, useState } from "react";
import GameBoard from "./GameBoard";
import { createHexLayoutSnapshot } from "./HexActionsLayoutVerification";
import LayoutPreviewFrame from "@/components/home/LayoutPreviewFrame";
import { useLayoutSettings } from "@/components/home/LayoutSettingsProvider";
import MobileFullscreenButton from "./MobileFullscreenButton";
import { loadCardSet } from "@/data/CardLoader";
import { GameRequest } from "@/net/GameRequest";
import { useGameStore } from "@/store/gameStore";
import { useSettingsStore } from "@/store/settingsStore";

const NUMBERS = ["OP01-016", "OP01-006", "OP01-025", "OP01-006", "OP01-013"];

/** 独立牌桌样本：拦截出牌与反击请求，验证整理后的服务器下标。 */
export default function CardSortLayoutVerification({ mobile, observer = false, crowded = false }: { mobile: boolean; observer?: boolean; crowded?: boolean }) {
  const [ready, setReady] = useState(false);
  const [request, setRequest] = useState("");
  const selected = useGameStore((state) => state.selectedHandIndex);
  const { setGameOverlayHost, suppressSettingsTrigger } = useLayoutSettings();
  const setOverlayHost = useCallback((host: HTMLDivElement | null) => {
    setGameOverlayHost(host, mobile);
  }, [mobile, setGameOverlayHost]);
  useEffect(() => suppressSettingsTrigger(), [suppressSettingsTrigger]);

  useEffect(() => {
    let cancelled = false;
    const previousSettings = useSettingsStore.getState();
    const originalPlay = GameRequest.playCard;
    const originalCounter = GameRequest.playCounterFromHand;
    const originalEndTurn = GameRequest.endTurn;
    GameRequest.playCard = (index) => { setRequest(`出牌:${index}`); return true; };
    GameRequest.playCounterFromHand = (index) => { setRequest(`反击:${index}`); return true; };
    GameRequest.endTurn = () => { setRequest("结束回合"); return true; };
    useSettingsStore.setState({ animationSpeed: "off", visualEffectsEnabled: false });
    loadCardSet("OP01").then(() => {
      if (cancelled) return;
      const store = useGameStore.getState();
      store.resetGame();
      store.setMode(observer ? "Observer" : "Player");
      const snapshot = createHexLayoutSnapshot();
      snapshot.matchKind = "Casual";
      snapshot.my.fieldCards = [];
      snapshot.my.leaderNumber = "OP01-001";
      snapshot.opponent.leaderNumber = "OP01-031";
      snapshot.my.handCardNumbers = [...NUMBERS];
      snapshot.my.handCardIds = NUMBERS.map((_, index) => `sort-hand-${index}`);
      snapshot.my.handCardCosts = [1, 1, 3, 1, 2];
      snapshot.my.handCardCounters = [1000, 2000, 0, 2000, 2000];
      snapshot.my.handCardCanPlay = NUMBERS.map(() => true);
      snapshot.my.handCount = NUMBERS.length;
      snapshot.my.costActive = 10;
      snapshot.my.trashNumbers = Array.from({ length: 50 }, (_, index) => NUMBERS[index % NUMBERS.length]);
      snapshot.opponent.trashNumbers = [...NUMBERS];
      // 对手手牌保留数量而不提供身份，检查整理入口不泄露隐藏信息。
      snapshot.opponent.handCount = 5;
      if (crowded) {
        snapshot.my.name = "整理手牌布局验证玩家·双行昵称";
        snapshot.opponent.name = "对手布局验证玩家·双行昵称";
        snapshot.operationClockEnabled = true;
        snapshot.myOperationTimeMs = snapshot.opponentOperationTimeMs = 900_000;
        snapshot.myTurnOperationTimeMs = snapshot.opponentTurnOperationTimeMs = 360_000;
        snapshot.operationClockActive = "my";
        snapshot.canUndoAttachDon = true;
        snapshot.undoAttachDonOperationId = "sort-layout-undo";
        snapshot.undoAttachDonCount = 1;
        snapshot.undoAttachDonDepth = 2;
      }
      if (observer) {
        snapshot.viewerKind = "spectator";
        snapshot.my.handCardIds = [];
        snapshot.my.handCardNumbers = [];
      }
      store.syncFromServer(snapshot);
      if (crowded) useGameStore.setState({ spectatorNames: ["布局观战者"] });
      setReady(true);
    });
    return () => {
      cancelled = true;
      GameRequest.playCard = originalPlay;
      GameRequest.playCounterFromHand = originalCounter;
      GameRequest.endTurn = originalEndTurn;
      useGameStore.getState().resetGame();
      useSettingsStore.setState({ animationSpeed: previousSettings.animationSpeed, visualEffectsEnabled: previousSettings.visualEffectsEnabled });
    };
  }, [observer, crowded]);

  return (
    <LayoutPreviewFrame mode={mobile ? "mobile-landscape" : "desktop"} rotateQuarterTurn={mobile} edgeToEdge>
      <main data-card-sort-verification data-card-sort-request={request} data-card-sort-selected={selected ?? ""} className="h-full w-full overflow-hidden bg-[#07111f]">
        {ready && <GameBoard isObserver={observer} isPlayback={false} onOpenFeedback={() => {}} />}
        <button hidden data-verification-counter onClick={() => useGameStore.setState((state) => ({
          phase: "Counter", currentTurn: false,
          battle: { attackerPlayer: 1, attackerCardId: state.opponent!.leaderId,
            targetIsLeader: true, targetCardId: null, blockerCardId: null, attackerBonus: 0, defenderBonus: 0 },
        }))}>进入反击</button>
        <button hidden data-verification-draw onClick={() => useGameStore.setState((state) => ({ my: state.my && {
          ...state.my, handCardIds: [...state.my.handCardIds, "sort-hand-new"],
          handCardNumbers: [...state.my.handCardNumbers, "OP01-006"],
          handCardCosts: [...state.my.handCardCosts, 1], handCardCounters: [...state.my.handCardCounters, 2000],
          handCardCanPlay: [...state.my.handCardCanPlay, true], handCount: state.my.handCount + 1,
        } }))}>模拟抽牌</button>
        <button hidden data-verification-remove onClick={() => useGameStore.getState().optimisticPlayCard(1)}>模拟移除手牌</button>
        <button hidden data-verification-empty onClick={() => useGameStore.setState((state) => ({ my: state.my && {
          ...state.my, handCardIds: [], handCardNumbers: [], handCardCosts: [], handCardCounters: [], handCardCanPlay: [], handCount: 0, trashNumbers: [],
        } }))}>清空区域</button>
      </main>
      <MobileFullscreenButton />
      <div ref={setOverlayHost} className="pointer-events-none absolute inset-0 z-[10000]" />
    </LayoutPreviewFrame>
  );
}
