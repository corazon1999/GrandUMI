"use client";

import { useCallback, useEffect, useRef, useState } from "react";
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
 * 返回资料就绪状态、失败提示和重试入口。
 */
export function useGameInit() {
  const [ready, setReady] = useState(false);
  // 卡集加载完成后自增 → 强制重渲染，使新抽到/获得的卡立即刷出卡图（不再需要点击）
  const [, bumpCardData] = useState(0);
  const my = useGameStore((s) => s.my);
  const opp = useGameStore((s) => s.opponent);
  const mounted = useRef(true);
  const kickedFullLoad = useRef(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [retryGeneration, setRetryGeneration] = useState(0);
  const retry = useCallback(() => {
    setLoadError(null);
    setRetryGeneration(value => value + 1);
  }, []);

  useEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; };
  }, []);

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
    // 准备阶段尚未发牌，提前读取己方已选择的卡组，避免首抽后再等待另一个卡集。
    if (typeof window !== "undefined") {
      try { allNums.push(...(sessionStorage.getItem("myDeck")?.match(/\b[A-Z][A-Z0-9]*-\d+\b/g) ?? [])); }
      catch { /* 禁用会话存储时仍可按快照加载。 */ }
    }

    const missing = new Set<string>();
    for (const n of allNums) {
      if (!getCard(n)) {
        const setCode = n.split("-")[0];
        if (setCode in CARD_SET_PATHS) missing.add(setCode);
      }
    }

    if (missing.size === 0) {
      setReady(true);
      setLoadError(null);
    } else {
      let cancelled = false;
      Promise.all([...missing].map(loadCardSet))
        .then(() => {
          if (!mounted.current || cancelled) return;
          setReady(true);
          setLoadError(null);
          bumpCardData((v) => v + 1);
        })
        .catch(() => {
          if (mounted.current && !cancelled) setLoadError("卡牌资料加载失败，请检查网络后重试。");
        });
      return () => { cancelled = true; };
    }

  }, [my, opp, retryGeneration]);

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

  return { ready, loadError, retry };
}
