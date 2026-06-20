"use client";

import { useEffect, useRef, useState } from "react";
import { useGameStore } from "@/store/gameStore";
import { getCard, loadCardSet, loadAllCards, applySpriteMap } from "@/data/CardLoader";
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
      applySessionSpriteMap();
      setReady(true);
    } else {
      Promise.all([...missing].map((p) => loadCardSet(p).catch(() => {})))
        .then(() => {
          if (!mounted.current) return;
          applySessionSpriteMap();
          setReady(true);
          bumpCardData((v) => v + 1);
        });
    }

    // 2) 后台一次性全量加载全部卡数据：牌库内容不在快照里，抽到/获得未加载卡集的卡时
    //    getCard 会返回空而显示卡背。预先全量加载杜绝此问题；allCards.json 内容哈希
    //    永久缓存，仅首次真正下载。完成后 bump 触发重渲染刷出卡图。
    if (!kickedFullLoad.current) {
      kickedFullLoad.current = true;
      loadAllCards()
        .then(() => {
          if (!mounted.current) return;
          applySessionSpriteMap();
          bumpCardData((v) => v + 1);
        })
        .catch(() => { kickedFullLoad.current = false; });
    }
  }, [my, opp]);

  return ready;
}

/** 从 sessionStorage 读取异画映射并应用到卡牌缓存 */
function applySessionSpriteMap() {
  try {
    const raw = sessionStorage.getItem("grandumi_spriteMap");
    if (raw) {
      const map = JSON.parse(raw) as Record<string, string>;
      applySpriteMap(map);
    }
  } catch {
    // 解析失败时忽略，使用默认原画
  }
}
