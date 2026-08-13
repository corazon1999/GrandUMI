"use client";

import { AnimatePresence, motion, useReducedMotion } from "framer-motion";
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import CardBack from "@/components/ui/CardBack";
import CardItem from "@/components/ui/CardItem";
import { useLayoutQuarterTurn } from "@/components/ui/ResponsiveScope";
import { getGameCard } from "@/data/CardLoader";
import {
  detectCardZoneTransitions,
  type CardZone,
  type CardZoneSide,
  type CardZoneTransition,
} from "@/lib/cardZoneTransitions";
import { viewportRectToLayerBounds } from "@/lib/stageGeometry";
import { useGameStore, type PlayerView } from "@/store/gameStore";
import { useSettingsStore } from "@/store/settingsStore";

const CARD_WIDTH = 72;
const CARD_HEIGHT = 101;
const MAX_ACTIVE_FLIGHTS = 24;

interface Bounds {
  left: number;
  top: number;
  width: number;
  height: number;
}

interface BoardFrame {
  tick: number;
  my: PlayerView | null;
  opponent: PlayerView | null;
  firstPlayerChosen: boolean;
  mulliganBothDone: boolean;
  turnCount: number;
  lastAction: string;
  actionPayload: Record<string, unknown> | null;
}

interface Flight extends CardZoneTransition {
  id: string;
  order: number;
  start: { x: number; y: number };
  end: { x: number; y: number };
  cardBackId?: string;
  spriteMap?: Record<string, string>;
}

function genericAnchorKey(side: CardZoneSide, zone: CardZone) {
  return `${side}:${zone}`;
}

function specificAnchorKey(
  side: CardZoneSide,
  zone: CardZone,
  cardId?: string,
  index?: number,
) {
  if (cardId) return `${side}:${zone}:card:${cardId}`;
  if (index != null) return `${side}:${zone}:index:${index}`;
  return genericAnchorKey(side, zone);
}

function toStageBounds(
  layer: HTMLElement,
  element: HTMLElement,
  rotateQuarterTurn: boolean,
): Bounds {
  return viewportRectToLayerBounds({
    layerRect: layer.getBoundingClientRect(),
    layerWidth: layer.clientWidth,
    layerHeight: layer.clientHeight,
    rotateQuarterTurn,
  }, element.getBoundingClientRect());
}

function collectBounds(layer: HTMLElement, rotateQuarterTurn: boolean) {
  const stage = layer.parentElement;
  const bounds = new Map<string, Bounds>();
  if (!stage) return bounds;

  stage.querySelectorAll<HTMLElement>("[data-zone][data-zone-side]").forEach((element) => {
    const side = element.dataset.zoneSide as CardZoneSide | undefined;
    const zone = element.dataset.zone as CardZone | undefined;
    if (!side || !zone) return;
    const measured = toStageBounds(layer, element, rotateQuarterTurn);
    const genericKey = genericAnchorKey(side, zone);
    if (!bounds.has(genericKey)) bounds.set(genericKey, measured);
    if (element.dataset.zoneCardId) {
      bounds.set(specificAnchorKey(side, zone, element.dataset.zoneCardId), measured);
    }
    if (element.dataset.zoneIndex != null && element.dataset.zoneIndex !== "") {
      const index = Number(element.dataset.zoneIndex);
      if (Number.isInteger(index)) bounds.set(specificAnchorKey(side, zone, undefined, index), measured);
    }
  });
  return bounds;
}

function transitionBounds(
  transition: CardZoneTransition,
  position: "source" | "target",
  primary: Map<string, Bounds>,
  fallback: Map<string, Bounds>,
) {
  const zone = position === "source" ? transition.from : transition.to;
  const cardId = position === "source" ? transition.sourceCardId : transition.targetCardId;
  const index = position === "source" ? transition.sourceIndex : transition.targetIndex;
  const specific = specificAnchorKey(transition.side, zone, cardId, index);
  const generic = genericAnchorKey(transition.side, zone);
  const specificBounds = primary.get(specific) ?? fallback.get(specific);
  const genericBounds = primary.get(generic) ?? fallback.get(generic);
  if (zone === "hand" && specificBounds && genericBounds) {
    // 手牌节点自身可能仍在播放入场 y 动画；横坐标取具体牌，纵坐标固定到手牌区底边的最终位置。
    return {
      ...specificBounds,
      top: genericBounds.top + genericBounds.height - specificBounds.height,
    };
  }
  return specificBounds ?? genericBounds ?? null;
}

function center(bounds: Bounds) {
  return {
    x: bounds.left + bounds.width / 2,
    y: bounds.top + bounds.height / 2,
  };
}

function gameplayReady(frame: BoardFrame) {
  return frame.firstPlayerChosen && (frame.mulliganBothDone || frame.turnCount > 0);
}

function bendPoint(start: Flight["start"], end: Flight["end"], order: number) {
  const dx = end.x - start.x;
  const dy = end.y - start.y;
  const distance = Math.max(1, Math.hypot(dx, dy));
  const bend = Math.min(78, Math.max(28, distance * 0.14)) * (order % 2 === 0 ? 1 : -1);
  return {
    x: (start.x + end.x) / 2 - (dy / distance) * bend,
    y: (start.y + end.y) / 2 + (dx / distance) * bend,
  };
}

function FlyingCard({
  flight,
  reducedMotion,
  fast,
  onComplete,
}: {
  flight: Flight;
  reducedMotion: boolean;
  fast: boolean;
  onComplete: (id: string) => void;
}) {
  const card = flight.cardNumber ? getGameCard(flight.cardNumber, flight.spriteMap) ?? null : null;
  const bend = bendPoint(flight.start, flight.end, flight.order);
  const startX = flight.start.x - CARD_WIDTH / 2;
  const startY = flight.start.y - CARD_HEIGHT / 2;
  const bendX = bend.x - CARD_WIDTH / 2;
  const bendY = bend.y - CARD_HEIGHT / 2;
  const endX = flight.end.x - CARD_WIDTH / 2;
  const endY = flight.end.y - CARD_HEIGHT / 2;
  const fromFlip = flight.fromFaceUp ? 0 : 180;
  const toFlip = flight.toFaceUp ? 0 : 180;
  const duration = reducedMotion ? 0.2 : fast ? 0.25 : 0.56;
  const delay = reducedMotion ? 0 : flight.order * (fast ? 0.02 : 0.045);

  return (
    <motion.div
      className="pointer-events-none absolute left-0 top-0 h-[6.3rem] w-[4.5rem]"
      style={{ perspective: 800 }}
      initial={{
        x: reducedMotion ? endX : startX,
        y: reducedMotion ? endY : startY,
        rotate: reducedMotion ? flight.toRotation : flight.fromRotation,
        opacity: 0,
        scale: reducedMotion ? 0.94 : 0.86,
      }}
      animate={{
        x: reducedMotion ? endX : [startX, bendX, endX],
        y: reducedMotion ? endY : [startY, bendY, endY],
        rotate: reducedMotion
          ? flight.toRotation
          : [flight.fromRotation, (flight.fromRotation + flight.toRotation) / 2 + (flight.order % 2 ? -5 : 5), flight.toRotation],
        opacity: [0, 1, 0],
        scale: reducedMotion ? [0.94, 1.03, 1] : [0.86, 1.08, 1],
      }}
      transition={{
        duration,
        delay,
        times: reducedMotion ? [0, 0.5, 1] : [0, 0.48, 1],
        ease: [0.22, 0.8, 0.24, 1],
      }}
      onAnimationComplete={() => onComplete(flight.id)}
      aria-hidden
    >
      <motion.div
        className="relative h-full w-full drop-shadow-[0_14px_16px_rgba(0,0,0,0.55)]"
        style={{ transformStyle: "preserve-3d" }}
        initial={{ rotateY: fromFlip }}
        animate={{ rotateY: toFlip }}
        transition={{ duration: reducedMotion ? 0.01 : 0.32, delay: delay + (reducedMotion ? 0 : 0.14) }}
      >
        <div className="absolute inset-0" style={{ backfaceVisibility: "hidden" }}>
          <CardItem
            card={card}
            size="sm"
            hideCounter
            hidePower
            hideCost
            liftOnSelect={false}
          />
        </div>
        <div
          className="absolute inset-0 overflow-hidden rounded-md"
          style={{ backfaceVisibility: "hidden", transform: "rotateY(180deg)" }}
        >
          <CardBack cardBackId={flight.cardBackId} decorative />
        </div>
      </motion.div>
    </motion.div>
  );
}

/**
 * 根据连续服务端快照推导卡牌跨区域移动，并在固定牌桌画布上播放飞牌与翻面动画。
 * 动画只消费展示层状态，不延迟或改写服务端权威牌桌。
 */
export default function CardZoneTransitionLayer() {
  const layerRef = useRef<HTMLDivElement>(null);
  const rotateQuarterTurn = useLayoutQuarterTurn();
  const previousFrameRef = useRef<BoardFrame | null>(null);
  const previousBoundsRef = useRef(new Map<string, Bounds>());
  const sequenceRef = useRef(0);
  const [flights, setFlights] = useState<Flight[]>([]);
  const reducedMotion = useReducedMotion() ?? false;
  const animationSpeed = useSettingsStore((state) => state.animationSpeed);

  useEffect(() => {
    if (animationSpeed === "off") setFlights([]);
  }, [animationSpeed]);

  const tick = useGameStore((state) => state.tick);
  const my = useGameStore((state) => state.my);
  const opponent = useGameStore((state) => state.opponent);
  const firstPlayerChosen = useGameStore((state) => state.firstPlayerChosen);
  const mulliganBothDone = useGameStore((state) => state.mulliganBothDone);
  const turnCount = useGameStore((state) => state.turnCount);
  const lastAction = useGameStore((state) => state.lastAction);
  const actionPayload = useGameStore((state) => state.lastActionPayloadObj);

  useLayoutEffect(() => {
    const layer = layerRef.current;
    if (!layer) return;
    const currentBounds = collectBounds(layer, rotateQuarterTurn);
    const currentFrame: BoardFrame = {
      tick,
      my,
      opponent,
      firstPlayerChosen,
      mulliganBothDone,
      turnCount,
      lastAction,
      actionPayload,
    };
    const previousFrame = previousFrameRef.current;
    const refreshBoundsAfterLayout = () => {
      let secondFrame = 0;
      const firstFrame = requestAnimationFrame(() => {
        secondFrame = requestAnimationFrame(() => {
          const state = useGameStore.getState();
          // 只刷新同一份权威快照；若期间发生乐观更新，必须保留动作前坐标。
          if (state.tick === tick && state.my === my && state.opponent === opponent) {
            previousBoundsRef.current = collectBounds(layer, rotateQuarterTurn);
          }
        });
      });
      return () => {
        cancelAnimationFrame(firstFrame);
        cancelAnimationFrame(secondFrame);
      };
    };

    if (!previousFrame) {
      previousFrameRef.current = currentFrame;
      previousBoundsRef.current = currentBounds;
      return refreshBoundsAfterLayout();
    }

    // 乐观更新沿用当前 tick；保留上一份权威快照和坐标，等服务端新 tick 到达后统一判定。
    if (tick === previousFrame.tick) return;

    // 回放倒退、退出对局或重新初始化时清空动画并重建基线，避免把整副牌误判为移动。
    if (tick < previousFrame.tick || !my || !opponent) {
      setFlights([]);
      previousFrameRef.current = currentFrame;
      previousBoundsRef.current = currentBounds;
      return refreshBoundsAfterLayout();
    }

    if (animationSpeed !== "off" && gameplayReady(previousFrame) && gameplayReady(currentFrame)) {
      const transitions = detectCardZoneTransitions(
        { my: previousFrame.my, opponent: previousFrame.opponent },
        { my, opponent },
        { lastAction, actionPayload },
      );
      const nextFlights = transitions.flatMap<Flight>((transition, order) => {
        const source = transitionBounds(
          transition,
          "source",
          previousBoundsRef.current,
          currentBounds,
        );
        const target = transitionBounds(transition, "target", currentBounds, previousBoundsRef.current);
        if (!source || !target) return [];
        return [{
          ...transition,
          id: `${tick}:${sequenceRef.current++}`,
          order,
          start: center(source),
          end: center(target),
          cardBackId: transition.side === "my" ? my.cardBackId : opponent.cardBackId,
          spriteMap: transition.side === "my" ? my.spriteMap : opponent.spriteMap,
        }];
      });
      if (nextFlights.length > 0) {
        setFlights((active) => [...active, ...nextFlights].slice(-MAX_ACTIVE_FLIGHTS));
      }
    }

    previousFrameRef.current = currentFrame;
    previousBoundsRef.current = currentBounds;
    return refreshBoundsAfterLayout();
  }, [
    actionPayload,
    animationSpeed,
    firstPlayerChosen,
    lastAction,
    mulliganBothDone,
    my,
    opponent,
    rotateQuarterTurn,
    tick,
    turnCount,
  ]);

  const finishFlight = useCallback((id: string) => {
    setFlights((active) => active.filter((flight) => flight.id !== id));
  }, []);

  return (
    <div ref={layerRef} className="pointer-events-none absolute inset-0 z-[35] overflow-visible">
      <AnimatePresence>
        {flights.map((flight) => (
          <FlyingCard
            key={flight.id}
            flight={flight}
            reducedMotion={reducedMotion}
            fast={animationSpeed === "fast"}
            onComplete={finishFlight}
          />
        ))}
      </AnimatePresence>
    </div>
  );
}
