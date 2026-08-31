"use client";

import { motion, AnimatePresence } from "framer-motion";
import { useEffect, useMemo, useRef, useState } from "react";
import { useGameStore } from "@/store/gameStore";
import { useResponsive } from "@/hooks/useResponsive";
import { useIsDefender } from "@/hooks/useIsDefender";
import { GameRequest } from "@/net/GameRequest";
import CardItem from "@/components/ui/CardItem";
import CounterEventConfirmModal, {
  type PendingCounterEvent,
} from "@/components/game/CounterEventConfirmModal";
import { getCard, getGameCard } from "@/data/CardLoader";
import { animationDuration, useSettingsStore } from "@/store/settingsStore";

interface Props {
  side: "my" | "opponent";
  hidden?: boolean;
}

export default function HandArea({ side, hidden = false }: Props) {
  const player = useGameStore((s) => (side === "my" ? s.my : s.opponent));
  const currentTurn = useGameStore((s) => s.currentTurn);
  const firstPlayerChosen = useGameStore((s) => s.firstPlayerChosen);
  const phase = useGameStore((s) => s.phase);
  const isPending = useGameStore((s) => s.isPending);
  const pendingPrompt = useGameStore((s) => s.pendingPrompt);
  const selectedHandIndex = useGameStore((s) => s.selectedHandIndex);
  const setSelectedHand = useGameStore((s) => s.setSelectedHand);
  const { cardSize } = useResponsive();
  const isDefender = useIsDefender();
  const animationSpeed = useSettingsStore((state) => state.animationSpeed);
  const [localOrder, setLocalOrder] = useState<string[]>([]);
  const [pendingCounterEvent, setPendingCounterEvent] = useState<PendingCounterEvent | null>(null);
  // 触控按下即临时抬高未选中的重叠手牌，让首次长按不依赖“先短按选中”。
  const [raisedTouchHandId, setRaisedTouchHandId] = useState<string | null>(null);
  const draggedIdRef = useRef<string | null>(null);
  const touchDragRef = useRef<{ id: string; startX: number; moved: boolean } | null>(null);
  const suppressClickRef = useRef(false);

  const serverHandIds = player?.handCardIds ?? [];
  const hasCompleteHandIds = !!player && serverHandIds.length === player.handCount;
  const orderedHandIds = useMemo(() => {
    if (!hasCompleteHandIds) return [];
    const retained = localOrder.filter((id) => serverHandIds.includes(id));
    return [...retained, ...serverHandIds.filter((id) => !retained.includes(id))];
  }, [hasCompleteHandIds, localOrder, serverHandIds]);

  useEffect(() => {
    if (!hasCompleteHandIds) {
      setLocalOrder((current) => current.length === 0 ? current : []);
      return;
    }
    setLocalOrder((current) => {
      const retained = current.filter((id) => serverHandIds.includes(id));
      const next = [...retained, ...serverHandIds.filter((id) => !retained.includes(id))];
      return current.length === next.length && current.every((id, index) => id === next[index])
        ? current
        : next;
    });
  }, [hasCompleteHandIds, serverHandIds]);

  // 测量手牌容器实际可用宽度（画布内为未缩放设计像素），用于动态计算重叠量
  const wrapRef = useRef<HTMLDivElement>(null);
  const [wrapW, setWrapW] = useState(0);
  useEffect(() => {
    const el = wrapRef.current;
    if (!el) return;
    // rAF 包裹 + 阈值防抖：避免 ResizeObserver 自反馈循环与亚像素抖动导致手牌反复重排（#143/#161）
    const update = () => {
      const w = el.clientWidth;
      setWrapW((prev) => (Math.abs(prev - w) > 1 ? w : prev));
    };
    update();
    let raf = 0;
    const ro = new ResizeObserver(() => {
      cancelAnimationFrame(raf);
      raf = requestAnimationFrame(update);
    });
    ro.observe(el);
    return () => { cancelAnimationFrame(raf); ro.disconnect(); };
  }, []);

  if (!player) return <div className="min-h-20" />;

  // 实时对局和观战按 handCount 生成卡背；回放合并终局时间线后，双方都会有完整牌号。
  // 旧回放缺少对手牌号时继续安全回退为卡背，不尝试推断隐藏信息。
  const hasCompleteHandIdentity = player.handCardNumbers.length === player.handCount;
  const serverCards = !firstPlayerChosen
    ? []
    : hidden
      ? Array.from({ length: player.handCount }, () => null)
      : hasCompleteHandIdentity
        ? player.handCardNumbers.map((n) => getGameCard(n, player.spriteMap) ?? null)
        : Array.from({ length: player.handCount }, () => null);
  const displayIndices = hasCompleteHandIds && !hidden
    ? orderedHandIds.map((id) => serverHandIds.indexOf(id)).filter((index) => index >= 0)
    : serverCards.map((_, index) => index);
  const cards = displayIndices.map((serverIndex) => serverCards[serverIndex]);

  // 稳定 key：按卡号 + 同名出现次序，不含数组下标。
  // 这样打出中间某张时，仅被移除那张的 key 消失，其余 key 不变 → 不会整手牌重排乱跳。
  const seen: Record<string, number> = {};
  const stableKeys = cards.map((card, i) => {
    const serverIndex = displayIndices[i];
    if (hasCompleteHandIds && serverHandIds[serverIndex]) return serverHandIds[serverIndex];
    const base = hasCompleteHandIdentity ? player.handCardNumbers[serverIndex] ?? "null" : "back";
    const occ = (seen[base] = (seen[base] ?? 0) + 1);
    return `${base}#${occ}`;
  });

  // 反击步骤：防守方可点击带反击值的手牌丢弃加反击；
  // 或点击「反击事件」（带 EventCounter 标签且费用可付）从手牌打出。
  // 防守方判断用 isDefender（攻击者属于对手），兼容 GM「对手领袖攻击」在我方回合制造的战斗。
  // 反击流程中的卡牌效果可能先打开另一个服务器选牌提示。
  // 该提示未处理完时，底层手牌只用于查看，不能把一次点击误发成反击弃牌请求。
  const isCounterStep =
    !hidden && side === "my" && phase === "Counter" && isDefender && pendingPrompt === null;
  const myActiveDon = side === "my" && player ? player.costActive : 0;
  const effectiveCounter = (c: ReturnType<typeof getCard> | null, i: number) =>
    (side === "my" ? player.handCardCounters?.[i] : undefined) ?? c?.counter ?? 0;

  // 反击事件是否可打：带 EventCounter 标签，且有效费用 ≤ 当前活跃咚
  const isCounterEventPlayable = (c: ReturnType<typeof getCard> | null, i: number) =>
    isCounterStep && !!c && effectiveCounter(c, i) <= 0 &&
    c.effectTags.includes("EventCounter") &&
    ((side === "my" ? player.handCardCosts?.[i] : undefined) ?? c.cost) <= myActiveDon;

  // 动态重叠：让手牌无论多少张都铺满且不溢出。
  // step = 每张相对前一张的水平步进（露出的宽度）；step<cardW 即重叠。
  const cardW = cardSize === "sm" ? 72 : cardSize === "md" ? 96 : 128; // CardItem 各档宽度(px)
  const GAP = 8;                       // 不重叠时的卡间距（≈gap-2）
  const PAD = 24;                      // 容器左右内边距(px-3 各 12)
  const minStep = Math.round(cardW * 0.22); // 重叠下限：每张至少露出 ~22%（手牌很多时也能铺下不溢出）
  const avail = Math.max(0, wrapW - PAD);
  const n = cards.length;
  let step = cardW + GAP;              // 默认：满间距、零重叠
  if (n > 1 && avail > 0) {
    const fitStep = (avail - cardW) / (n - 1);
    step = Math.max(minStep, Math.min(cardW + GAP, fitStep));
  }
  step = Math.round(step);             // 整数步进，杜绝亚像素
  // 整数像素绝对定位（替代 flex 居中 + 负 marginLeft + framer-motion layout）：
  // 每张卡的 x 由整数公式算出，motion.div 用 animate={{x}} 平移到位，不挂 layout、
  // 不依赖浏览器 justify-center → 无亚像素居中、无 layout 反复测量 → 手牌再多也不漂移（#161）。
  const totalW = cardW + (n - 1) * step;
  const startX = Math.round(Math.max(PAD / 2, (wrapW - totalW) / 2)); // 居中起点；溢出则贴左内边距
  const xOf = (i: number) => startX + i * step;                       // 已是整数

  const handleClick = (i: number) => {
    if (suppressClickRef.current) {
      suppressClickRef.current = false;
      return;
    }
    if (hidden || isPending) return;
    if (isCounterStep) {
      const c = serverCards[i];
      if (c && effectiveCounter(c, i) > 0) GameRequest.playCounterFromHand(i);
      else if (isCounterEventPlayable(c, i) && c) {
        setPendingCounterEvent({
          handIndex: i,
          cardNumber: c.number,
          cardName: c.name,
          cost: player.handCardCosts?.[i] ?? c.cost,
        });
      }
      return;
    }
    if (side !== "my" || !currentTurn) return;
    setSelectedHand(selectedHandIndex === i ? null : i);
  };

  const moveHandCard = (sourceId: string, targetId: string) => {
    if (sourceId === targetId) return;
    setLocalOrder((current) => {
      const sourceIndex = current.indexOf(sourceId);
      const targetIndex = current.indexOf(targetId);
      if (sourceIndex < 0 || targetIndex < 0) return current;
      const next = [...current];
      next.splice(sourceIndex, 1);
      next.splice(targetIndex, 0, sourceId);
      return next;
    });
  };

  return (
    <>
      <div
      ref={wrapRef}
      className="relative z-30 h-full w-full min-w-0 overflow-visible"
      data-zone="hand"
      data-zone-side={side}
    >
      <AnimatePresence>
        {cards.map((card, i) => {
          const serverIndex = displayIndices[i];
          const handId = hasCompleteHandIds ? serverHandIds[serverIndex] : null;
          const counterPlayable =
            (isCounterStep && effectiveCounter(card, serverIndex) > 0)
              || isCounterEventPlayable(card, serverIndex);
          return (
            <motion.div
              key={stableKeys[i]}
              data-zone="hand"
              data-zone-side={side}
              data-zone-index={serverIndex}
              data-hand-order-id={handId ?? undefined}
              draggable={side === "my" && !hidden && !!handId}
              aria-grabbed={draggedIdRef.current === handId}
              title={side === "my" && !hidden && handId ? "可左右拖动调整手牌显示顺序" : undefined}
              onDragStartCapture={(event) => {
                if (!handId) return;
                draggedIdRef.current = handId;
                event.dataTransfer.effectAllowed = "move";
                event.dataTransfer.setData("text/plain", handId);
              }}
              onDragOverCapture={(event) => {
                if (draggedIdRef.current && handId) event.preventDefault();
              }}
              onDragEnterCapture={() => {
                if (draggedIdRef.current && handId) moveHandCard(draggedIdRef.current, handId);
              }}
              onDragEndCapture={() => { draggedIdRef.current = null; }}
              onPointerDown={(event) => {
                if (!handId || event.pointerType === "mouse") return;
                touchDragRef.current = { id: handId, startX: event.clientX, moved: false };
                setRaisedTouchHandId(handId);
                event.currentTarget.setPointerCapture(event.pointerId);
              }}
              onPointerMove={(event) => {
                const drag = touchDragRef.current;
                if (!drag || event.pointerType === "mouse") return;
                if (!drag.moved && Math.abs(event.clientX - drag.startX) < 8) return;
                drag.moved = true;
                suppressClickRef.current = true;
                const target = document.elementsFromPoint(event.clientX, event.clientY)
                  .map((element) => element.closest<HTMLElement>("[data-hand-order-id]"))
                  .find((element) => element?.dataset.handOrderId);
                if (target?.dataset.handOrderId) moveHandCard(drag.id, target.dataset.handOrderId);
              }}
              onPointerUp={(event) => {
                if (touchDragRef.current?.moved) event.preventDefault();
                touchDragRef.current = null;
                setRaisedTouchHandId(null);
              }}
              onPointerCancel={() => {
                touchDragRef.current = null;
                setRaisedTouchHandId(null);
              }}
              onLostPointerCapture={() => { setRaisedTouchHandId(null); }}
              initial={{ x: xOf(i), y: side === "my" ? 36 : -24, opacity: 0 }}
              animate={{ x: xOf(i), y: 0, opacity: 1 }}
              exit={{ y: side === "my" ? -24 : 24, opacity: 0 }}
              transition={{
                x: animationSpeed === "off"
                  ? { duration: 0 }
                  : { type: "spring", stiffness: 350, damping: 30 },
                y: animationSpeed === "off"
                  ? { duration: 0 }
                  : { delay: animationDuration(i * 40, animationSpeed) / 1000, type: "spring", stiffness: 200 },
                opacity: { delay: animationDuration(i * 40, animationSpeed) / 1000 },
              }}
              style={{ position: "absolute", left: 0, bottom: 0 }}
              className={[
                "hover:z-20", // 悬停提升层级，盖过邻牌，便于看清被压住的卡（低于右栏操作区 z-30，避免盖住出牌/结束回合按钮）
                raisedTouchHandId === handId ? "z-20" : "",
                side === "my" && !hidden && handId ? "touch-none cursor-grab active:cursor-grabbing" : "",
                counterPlayable ? "rounded-md ring-2 ring-amber-400 animate-pulse" : "",
              ].join(" ")}
            >
              <CardItem
                card={card}
                isSelected={!hidden && selectedHandIndex === serverIndex}
                faceDown={hidden || card === null}
                cardBackId={player.cardBackId}
                cardBackSide={side}
                hidePower
                counterValue={effectiveCounter(card, serverIndex)}
                onClick={() => handleClick(serverIndex)}
                size={cardSize}
                costBuff={
                  side === "my" && card && player.handCardCosts?.[serverIndex] != null
                    ? player.handCardCosts[serverIndex] - card.cost
                    : 0
                }
              />
            </motion.div>
          );
        })}
      </AnimatePresence>

      {cards.length === 0 && (
        <span className="absolute inset-0 flex items-center justify-center text-xs text-gray-700">
          {hidden ? "手牌" : "手牌为空"}
        </span>
      )}

      {/* #188 手牌数量角标：我方底部/对手顶部，避开右栏(left 定位)，z-30 + pointer-events-none 不遮点击 */}
      <span
        className={[
          "pointer-events-none absolute left-1 z-30",
          "rounded-full bg-black/60 px-2 py-0.5 text-xs font-bold text-white ring-1 ring-white/20",
          side === "my" ? "bottom-1" : "top-1",
        ].join(" ")}
      >
        {cards.length}
      </span>
      </div>

      <CounterEventConfirmModal
        pending={pendingCounterEvent}
        onCancel={() => setPendingCounterEvent(null)}
        onConfirm={() => {
          const pending = pendingCounterEvent;
          setPendingCounterEvent(null);
          if (!pending || !isCounterStep || isPending) return;
          if (serverCards[pending.handIndex]?.number !== pending.cardNumber) return;
          GameRequest.playCounterEvent(pending.handIndex);
        }}
      />
    </>
  );
}
