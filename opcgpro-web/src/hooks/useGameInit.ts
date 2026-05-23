"use client";

import { useEffect, useState } from "react";
import { useGameStore } from "@/store/gameStore";
import { getCard, loadCardSet } from "@/data/CardLoader";
import { CARD_SET_PATHS } from "@/data/cardSets";

/**
 * 游戏对局初始化（新架构）
 *
 * 服务器是状态权威源。客户端只需要：
 *   1. 等首份 MsgGameState 到达（my != null）
 *   2. 按需懒加载卡集 JSON（用于显示卡面信息）
 *
 * 返回 true 表示可以显示对战界面
 */
export function useGameInit() {
  const [ready, setReady] = useState(false);
  const my = useGameStore((s) => s.my);
  const opp = useGameStore((s) => s.opponent);

  useEffect(() => {
    if (!my || !opp) return;

    // 收集快照中出现的卡号，按需加载对应卡集
    const allNums = [
      my.leaderNumber, opp.leaderNumber,
      ...my.handCardNumbers,
      ...my.fieldCards.map((c) => c.number),
      ...opp.fieldCards.map((c) => c.number),
      ...my.trashNumbers, ...opp.trashNumbers,
      my.stageNumber, opp.stageNumber,
    ].filter((n): n is string => !!n);

    const missing = new Set<string>();
    for (const n of allNums) {
      if (!getCard(n)) {
        const setCode = n.split("-")[0];
        if (setCode in CARD_SET_PATHS) missing.add(setCode);
      }
    }

    if (missing.size === 0) {
      setReady(true);
      return;
    }

    Promise.all([...missing].map((p) => loadCardSet(p).catch(() => {})))
      .then(() => setReady(true));
  }, [my, opp]);

  return ready;
}
