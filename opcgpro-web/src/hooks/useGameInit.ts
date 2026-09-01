"use client";

import { useEffect, useRef, useState } from "react";
import { useGameStore } from "@/store/gameStore";
import { getCard, loadCardSet, loadAllCards } from "@/data/CardLoader";
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
  // 卡集加载完成后自增 → 强制重渲染，使新抽到/获得的卡立即刷出卡图（不再需要点击）
  const [, bumpCardData] = useState(0);
  const my = useGameStore((s) => s.my);
  const opp = useGameStore((s) => s.opponent);
  const mounted = useRef(true);
  const kickedFullLoad = useRef(false);

  useEffect(() => () => { mounted.current = false; }, []);

  useEffect(() => {
    if (!my || !opp) return;

    // 1) 收集快照中出现的卡号，按需加载对应卡集，使板面尽快就绪
    const allNums = [
      my.leaderNumber, opp.leaderNumber,
      ...my.handCardNumbers,
      ...my.fieldCards.map((c) => c.number),
      ...opp.fieldCards.map((c) => c.number),
      ...my.trashNumbers, ...opp.trashNumbers,
      ...my.stages.map((stage) => stage.number),
      ...opp.stages.map((stage) => stage.number),
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
    } else {
      Promise.all([...missing].map((p) => loadCardSet(p).catch(() => {})))
        .then(() => {
          if (!mounted.current) return;
          setReady(true);
          bumpCardData((v) => v + 1);
        });
    }

  }, [my, opp]);

  // 首屏所需卡集就绪后再后台加载 1.3MB 全卡包，避免它与第一批卡图争抢带宽。
  // 先留出 800ms 给对战界面和 WebP 小图，再利用空闲时段启动；最迟约 2 秒执行。
  useEffect(() => {
    if (!ready || kickedFullLoad.current) return;
    kickedFullLoad.current = true;

    let idleId: number | null = null;
    let started = false;
    const startFullLoad = () => {
      started = true;
      loadAllCards()
        .then(() => {
          if (!mounted.current) return;
          bumpCardData((v) => v + 1);
        })
        .catch(() => { kickedFullLoad.current = false; });
    };

    const delayId = window.setTimeout(() => {
      if ("requestIdleCallback" in window) {
        idleId = window.requestIdleCallback(startFullLoad, { timeout: 1200 });
      } else {
        startFullLoad();
      }
    }, 800);

    return () => {
      window.clearTimeout(delayId);
      if (idleId !== null && "cancelIdleCallback" in window) {
        window.cancelIdleCallback(idleId);
      }
      if (!started) kickedFullLoad.current = false;
    };
  }, [ready]);

  return ready;
}
