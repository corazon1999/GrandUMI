"use client";

import { useEffect, useState } from "react";
import GameBoard from "@/components/game/GameBoard";
import { createHexLayoutSnapshot } from "@/components/game/HexActionsLayoutVerification";
import LayoutPreviewRoute from "@/components/home/LayoutPreviewRoute";
import { loadAllCards } from "@/data/CardLoader";
import { useGameStore } from "@/store/gameStore";

/** 使用真实牌桌和自动旋转布局验证设置，不连接真实对局。 */
export default function VisualEffectsLayoutVerification() {
  const [ready, setReady] = useState(false);
  useEffect(() => {
    let disposed = false;
    void loadAllCards().then(() => {
      if (disposed) return;
      const state = createHexLayoutSnapshot();
      state.my.fieldCards[0].gainedKeywords = ["阻挡者", "速攻", "双重攻击", "不可阻挡", "流放", "可攻击活跃"];
      state.battle = {
        attackerPlayer: 0, attackerCardId: state.my.fieldCards[0].id,
        targetIsLeader: true, targetCardId: null, blockerCardId: null,
        attackerBonus: 0, defenderBonus: 0,
      };
      useGameStore.getState().resetGame();
      useGameStore.getState().syncFromServer(state);
      setReady(true);
    });
    return () => { disposed = true; useGameStore.getState().resetGame(); };
  }, []);
  return (
    <LayoutPreviewRoute>
      <main data-visual-effects-verification className="h-full w-full bg-[#07111f]">
        {ready && <GameBoard isObserver={false} isPlayback={false} />}
      </main>
    </LayoutPreviewRoute>
  );
}
