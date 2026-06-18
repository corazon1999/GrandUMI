"use client";

import { motion, AnimatePresence } from "framer-motion";
import { useEffect, useRef, useState } from "react";
import { useGameStore } from "@/store/gameStore";
import { useResponsive } from "@/hooks/useResponsive";
import { useIsDefender } from "@/hooks/useIsDefender";
import { GameRequest } from "@/net/GameRequest";
import CardItem from "@/components/ui/CardItem";
import { getCard } from "@/data/CardLoader";

interface Props {
  side: "my" | "opponent";
  hidden?: boolean;
}

export default function HandArea({ side, hidden = false }: Props) {
  const player = useGameStore((s) => (side === "my" ? s.my : s.opponent));
  const currentTurn = useGameStore((s) => s.currentTurn);
  const phase = useGameStore((s) => s.phase);
  const isPending = useGameStore((s) => s.isPending);
  const selectedHandIndex = useGameStore((s) => s.selectedHandIndex);
  const setSelectedHand = useGameStore((s) => s.setSelectedHand);
  const { cardSize } = useResponsive();
  const isDefender = useIsDefender();

  // 测量手牌容器实际可用宽度（画布内为未缩放设计像素），用于动态计算重叠量
  const wrapRef = useRef<HTMLDivElement>(null);
  const [wrapW, setWrapW] = useState(0);
  useEffect(() => {
    const el = wrapRef.current;
    if (!el) return;
    const update = () => setWrapW(el.clientWidth);
    update();
    const ro = new ResizeObserver(update);
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  if (!player) return <div className="min-h-20" />;

  const cards = side === "my"
    ? player.handCardNumbers.map((n) => getCard(n) ?? null)
    : Array.from({ length: player.handCount }, () => null);

  // 稳定 key：按卡号 + 同名出现次序，不含数组下标。
  // 这样打出中间某张时，仅被移除那张的 key 消失，其余 key 不变 → 不会整手牌重排乱跳。
  const seen: Record<string, number> = {};
  const stableKeys = cards.map((card, i) => {
    const base = side === "my" ? player.handCardNumbers[i] ?? "null" : "back";
    const occ = (seen[base] = (seen[base] ?? 0) + 1);
    return `${base}#${occ}`;
  });

  // 反击步骤：防守方可点击带反击值的手牌丢弃加反击；
  // 或点击「反击事件」（带 EventCounter 标签且费用可付）从手牌打出。
  // 防守方判断用 isDefender（攻击者属于对手），兼容 GM「对手领袖攻击」在我方回合制造的战斗。
  const isCounterStep = !hidden && side === "my" && phase === "Counter" && isDefender;
  const myActiveDon = side === "my" && player ? player.costActive : 0;

  // 反击事件是否可打：带 EventCounter 标签，且有效费用 ≤ 当前活跃咚
  const isCounterEventPlayable = (c: ReturnType<typeof getCard> | null, i: number) =>
    isCounterStep && !!c && (c.counter ?? 0) <= 0 &&
    c.effectTags.includes("EventCounter") &&
    ((side === "my" ? player.handCardCosts?.[i] : undefined) ?? c.cost) <= myActiveDon;

  // 动态重叠：让手牌无论多少张都铺满且不溢出。
  // step = 每张相对前一张的水平步进（露出的宽度）；step<cardW 即重叠。
  const cardW = cardSize === "sm" ? 72 : cardSize === "md" ? 96 : 128; // CardItem 各档宽度(px)
  const GAP = 8;                       // 不重叠时的卡间距（≈gap-2）
  const PAD = 24;                      // 容器左右内边距(px-3 各 12)
  const minStep = Math.round(cardW * 0.3); // 重叠下限：每张至少露出 ~30% 便于悬停
  const avail = Math.max(0, wrapW - PAD);
  const n = cards.length;
  let step = cardW + GAP;              // 默认：满间距、零重叠
  if (n > 1 && avail > 0) {
    const fitStep = (avail - cardW) / (n - 1);
    step = Math.max(minStep, Math.min(cardW + GAP, fitStep));
  }
  const marginLeft = step - cardW;     // 负值=重叠；正值=普通间距

  const handleClick = (i: number) => {
    if (hidden || isPending) return;
    if (isCounterStep) {
      const c = cards[i];
      if (c && c.counter > 0) GameRequest.playCounterFromHand(i);
      else if (isCounterEventPlayable(c, i)) GameRequest.playCounterEvent(i);
      return;
    }
    if (side !== "my" || !currentTurn) return;
    setSelectedHand(selectedHandIndex === i ? null : i);
  };

  return (
    <div
      ref={wrapRef}
      className="flex min-h-24 w-full min-w-0 items-end justify-center overflow-x-auto px-3 py-6 -my-5 lg:min-h-32"
    >
      <AnimatePresence>
        {cards.map((card, i) => {
          const counterPlayable =
            (isCounterStep && (card?.counter ?? 0) > 0) || isCounterEventPlayable(card, i);
          return (
            <motion.div
              key={stableKeys[i]}
              layout
              initial={{ y: side === "my" ? 36 : -24, opacity: 0 }}
              animate={{ y: 0, opacity: 1 }}
              exit={{ y: side === "my" ? -24 : 24, opacity: 0 }}
              transition={{
                default: { delay: i * 0.04, type: "spring", stiffness: 200 },
                layout: { type: "spring", stiffness: 350, damping: 30 },
              }}
              style={{ marginLeft: i === 0 ? 0 : marginLeft }}
              className={[
                "relative hover:z-20", // 悬停提升层级，盖过邻牌，便于看清被压住的卡（低于右栏操作区 z-30，避免盖住出牌/结束回合按钮）
                counterPlayable ? "rounded-md ring-2 ring-amber-400 animate-pulse" : "",
              ].join(" ")}
            >
              <CardItem
                card={card}
                isSelected={!hidden && selectedHandIndex === i}
                faceDown={hidden || card === null}
                hidePower
                onClick={() => handleClick(i)}
                size={cardSize}
                costBuff={
                  side === "my" && card && player.handCardCosts?.[i] != null
                    ? player.handCardCosts[i] - card.cost
                    : 0
                }
              />
            </motion.div>
          );
        })}
      </AnimatePresence>

      {cards.length === 0 && (
        <span className="text-xs text-gray-700">
          {hidden ? "对手手牌" : "手牌为空"}
        </span>
      )}
    </div>
  );
}
