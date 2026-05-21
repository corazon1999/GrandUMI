"use client";

import { useEffect, useState } from "react";
import { useGameStore } from "@/store/gameStore";
import { getCard, loadCardSet } from "@/data/CardLoader";
import { CARD_SET_PATHS } from "@/data/cardSets";

/**
 * 游戏对局初始化 hook
 * 对应 C# Deck.LoadMyDeck() → Leader.Init() → Life.InitLife() → Deck.SelfInit()
 *
 * 流程：
 *   1. 从 sessionStorage 读取双方卡组字符串（由 handleGameStart 写入）
 *   2. 提取卡号中的卡集前缀，按需加载卡集数据
 *   3. 调用 gameStore.initFromDecks() 完成领航卡/生命/手牌/卡组初始化
 */
export function useGameInit() {
  const [ready, setReady] = useState(false);
  const isGameOver = useGameStore((s) => s.isGameOver);
  const leader = useGameStore((s) => s.my.leader);

  useEffect(() => {
    if (isGameOver || leader) return;

    const myDeck = sessionStorage.getItem("myDeck") ?? "";
    const enemyDeck = sessionStorage.getItem("enemyDeck") ?? "";
    if (!myDeck || !enemyDeck) return;

    const isFirst = sessionStorage.getItem("isFirst") === "1";

    const allNums = [...myDeck.split("\n"), ...enemyDeck.split("\n")].filter(Boolean);
    const prefixes = new Set(allNums.map((n) => n.split("-")[0]));
    const toLoad = [...prefixes].filter((p) => p in CARD_SET_PATHS && !getCard(allNums.find((n) => n.startsWith(p))!));

    const init = async () => {
      if (toLoad.length > 0) {
        await Promise.all(toLoad.map((p) => loadCardSet(p).catch(() => {})));
      }
      useGameStore.getState().initFromDecks(myDeck, enemyDeck, isFirst);
      setReady(true);
    };

    init();
  }, [isGameOver, leader]);

  return ready;
}
