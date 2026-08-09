"use client";

import { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { AnimatePresence, motion, useReducedMotion } from "framer-motion";
import AttributeAttackEffect from "@/components/game/AttributeAttackEffect";
import { getCard } from "@/data/CardLoader";
import {
  ATTACK_ATTRIBUTE_THEMES,
  composeAttackTheme,
  normalizeAttackAttributes,
  type AttackAttribute,
} from "@/lib/attackAttributeEffects";
import { useGameStore, type PlayerView } from "@/store/gameStore";

interface Point {
  x: number;
  y: number;
}

interface Combatant {
  id: string;
  name: string;
  power: number;
  side: "my" | "opponent";
  attributes: AttackAttribute[];
}

function findCombatant(id: string | null, my: PlayerView | null, opponent: PlayerView | null): Combatant | null {
  if (!id) return null;

  for (const [side, player] of [["my", my], ["opponent", opponent]] as const) {
    if (!player) continue;
    if (player.leaderId === id) {
      const card = getCard(player.leaderNumber);
      return {
        id,
        name: card?.name ?? "领袖",
        power: player.leaderPower,
        side,
        attributes: normalizeAttackAttributes(card?.property),
      };
    }
    const fieldCard = player.fieldCards.find((card) => card.id === id);
    if (fieldCard) {
      const card = getCard(fieldCard.number);
      return {
        id,
        name: card?.name ?? "角色",
        power: fieldCard.powerCurrent,
        side,
        attributes: normalizeAttackAttributes(card?.property),
      };
    }
  }

  return null;
}

function cardCenter(layer: HTMLElement, cardId: string): Point | null {
  const stage = layer.parentElement;
  if (!stage) return null;

  const card = Array.from(stage.querySelectorAll<HTMLElement>("[data-battle-card-id]"))
    .find((element) => element.dataset.battleCardId === cardId);
  if (!card) return null;

  const layerRect = layer.getBoundingClientRect();
  const cardRect = card.getBoundingClientRect();
  const scaleX = layerRect.width / layer.clientWidth || 1;
  const scaleY = layerRect.height / layer.clientHeight || 1;

  return {
    x: (cardRect.left - layerRect.left + cardRect.width / 2) / scaleX,
    y: (cardRect.top - layerRect.top + cardRect.height / 2) / scaleY,
  };
}

function attackPath(source: Point, target: Point) {
  const dx = target.x - source.x;
  const dy = target.y - source.y;
  const distance = Math.max(1, Math.hypot(dx, dy));
  const ux = dx / distance;
  const uy = dy / distance;
  const edgePadding = Math.min(42, distance * 0.16);
  const start = { x: source.x + ux * edgePadding, y: source.y + uy * edgePadding };
  const end = { x: target.x - ux * edgePadding, y: target.y - uy * edgePadding };
  const bend = Math.min(68, distance * 0.14) * (source.x <= target.x ? 1 : -1);
  const control = {
    x: (start.x + end.x) / 2 - uy * bend,
    y: (start.y + end.y) / 2 + ux * bend,
  };

  return `M ${start.x} ${start.y} Q ${control.x} ${control.y} ${end.x} ${end.y}`;
}

function powerLabel(value: number) {
  return value.toLocaleString("zh-CN");
}

/**
 * 在牌桌设计画布内连接攻击者与当前有效目标。
 * battle 快照持续存在时保留流动轨迹；阻挡者替换目标后，轨迹自动改向。
 */
export default function BattleRelationLayer() {
  const layerRef = useRef<HTMLDivElement>(null);
  const previousRouteRef = useRef("");
  const battle = useGameStore((state) => state.battle);
  const my = useGameStore((state) => state.my);
  const opponent = useGameStore((state) => state.opponent);
  const tick = useGameStore((state) => state.tick);
  const reduceMotion = useReducedMotion();
  const [points, setPoints] = useState<{ source: Point; target: Point } | null>(null);

  const attacker = useMemo(
    () => findCombatant(battle?.attackerCardId ?? null, my, opponent),
    [battle?.attackerCardId, my, opponent],
  );

  const originalTargetId = useMemo(() => {
    if (!battle || !attacker) return null;
    if (!battle.targetIsLeader) return battle.targetCardId;
    return attacker.side === "my" ? opponent?.leaderId ?? null : my?.leaderId ?? null;
  }, [attacker, battle, my?.leaderId, opponent?.leaderId]);

  const effectiveTargetId = battle?.blockerCardId ?? originalTargetId;
  const target = useMemo(
    () => findCombatant(effectiveTargetId, my, opponent),
    [effectiveTargetId, my, opponent],
  );
  const isBlocked = Boolean(battle?.blockerCardId);
  const routeKey = battle && effectiveTargetId
    ? `${battle.attackerCardId}:${effectiveTargetId}`
    : "";

  useLayoutEffect(() => {
    const layer = layerRef.current;
    if (!layer || !battle || !effectiveTargetId) {
      previousRouteRef.current = "";
      setPoints(null);
      return;
    }

    if (previousRouteRef.current !== routeKey) {
      previousRouteRef.current = routeKey;
      setPoints(null);
    }

    let frame = 0;
    const update = () => {
      const source = cardCenter(layer, battle.attackerCardId);
      const targetPoint = cardCenter(layer, effectiveTargetId);
      setPoints(source && targetPoint ? { source, target: targetPoint } : null);
    };
    const scheduleUpdate = () => {
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(update);
    };

    scheduleUpdate();
    window.addEventListener("resize", scheduleUpdate);
    const observer = new ResizeObserver(scheduleUpdate);
    observer.observe(layer);

    return () => {
      cancelAnimationFrame(frame);
      window.removeEventListener("resize", scheduleUpdate);
      observer.disconnect();
    };
  }, [battle, effectiveTargetId, routeKey, tick]);

  // 卡牌横置动画会改变可见外形；在声明/阻挡快照渲染完成后再校准一次端点。
  useEffect(() => {
    if (!battle) return;
    const timeout = window.setTimeout(() => {
      const layer = layerRef.current;
      if (!layer || !effectiveTargetId) return;
      const source = cardCenter(layer, battle.attackerCardId);
      const targetPoint = cardCenter(layer, effectiveTargetId);
      if (source && targetPoint) setPoints({ source, target: targetPoint });
    }, 760);
    return () => window.clearTimeout(timeout);
  }, [battle, effectiveTargetId, tick]);

  const path = points ? attackPath(points.source, points.target) : "";
  const attackerPower = attacker && battle ? attacker.power + battle.attackerBonus : 0;
  const targetPower = target && battle ? target.power + battle.defenderBonus : 0;
  const attackTheme = composeAttackTheme(attacker?.attributes ?? ["?"]);
  const shouldReduceMotion = Boolean(reduceMotion);

  return (
    <div ref={layerRef} className="pointer-events-none absolute inset-0 z-30" aria-hidden={!battle}>
      <AnimatePresence>
        {battle && attacker && target && (
          <motion.div
            key={`${battle.attackerCardId}:${originalTargetId ?? "leader"}`}
            role="status"
            aria-live="polite"
            className="absolute left-1/2 top-[322px] z-10 flex -translate-x-1/2 items-center gap-2 rounded-full border border-orange-200/40 bg-slate-950/95 px-4 py-2 text-xs font-black text-white shadow-2xl shadow-black/70 backdrop-blur"
            initial={reduceMotion ? false : { opacity: 0, y: 8, scale: 0.9 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: -5, scale: 0.96 }}
            transition={{ duration: reduceMotion ? 0 : 0.2 }}
          >
            <span className="max-w-40 truncate text-red-200">
              {attacker.side === "my" ? "我方" : "对手"} · {attacker.name}
            </span>
            <span className="rounded bg-red-500/20 px-1.5 py-0.5 text-red-100">
              {powerLabel(attackerPower)}
            </span>
            <span className="flex items-center gap-1" aria-label={`攻击属性 ${attackTheme.label}`}>
              {attackTheme.attributes.map((attribute) => {
                const theme = ATTACK_ATTRIBUTE_THEMES[attribute];
                return (
                  <span
                    key={attribute}
                    className="rounded-full border px-1.5 py-0.5 text-[10px] leading-none shadow-sm"
                    style={{
                      color: theme.accent,
                      borderColor: `${theme.primary}70`,
                      backgroundColor: `${theme.secondary}28`,
                    }}
                  >
                    {theme.label}
                  </span>
                );
              })}
            </span>
            <span className="text-base text-orange-300">⚔</span>
            <span className="max-w-40 truncate text-amber-100">
              {target.side === "my" ? "我方" : "对手"} · {target.name}
            </span>
            <span className="rounded bg-amber-400/15 px-1.5 py-0.5 text-amber-100">
              {powerLabel(targetPower)}
            </span>
            {isBlocked && (
              <span className="rounded-full bg-cyan-400/20 px-2 py-0.5 text-[10px] text-cyan-200 ring-1 ring-cyan-300/40">
                阻挡
              </span>
            )}
          </motion.div>
        )}
      </AnimatePresence>

      <svg className="absolute inset-0 h-full w-full overflow-visible" viewBox="0 0 1280 720">
        <defs>
          <filter id="battle-route-glow" x="-80%" y="-80%" width="260%" height="260%">
            <feGaussianBlur stdDeviation="5" result="blur" />
            <feMerge>
              <feMergeNode in="blur" />
              <feMergeNode in="SourceGraphic" />
            </feMerge>
          </filter>
          <filter id="battle-route-bloom" x="-100%" y="-100%" width="300%" height="300%">
            <feGaussianBlur stdDeviation="11" />
          </filter>
          <linearGradient id="battle-route-attribute" x1="0" y1="0" x2="1" y2="0">
            {attackTheme.colors.map((color, index) => (
              <stop
                key={`${color}:${index}`}
                offset={`${(index / Math.max(1, attackTheme.colors.length - 1)) * 100}%`}
                stopColor={color}
              />
            ))}
          </linearGradient>
          <linearGradient id="battle-route-block" x1="0" y1="0" x2="1" y2="0">
            <stop offset="0" stopColor="#f97316" />
            <stop offset="0.58" stopColor="#facc15" />
            <stop offset="1" stopColor="#67e8f9" />
          </linearGradient>
        </defs>

        <AnimatePresence mode="popLayout">
          {points && path && (
            <motion.g key={routeKey}>
              <motion.path
                d={path}
                fill="none"
                stroke={isBlocked ? "#22d3ee" : attackTheme.primary}
                strokeWidth="30"
                strokeLinecap="round"
                opacity="0.085"
                filter="url(#battle-route-bloom)"
                initial={reduceMotion ? false : { opacity: 0, pathLength: 0 }}
                animate={{ opacity: 0.085, pathLength: 1 }}
                transition={{ duration: reduceMotion ? 0 : 0.38, ease: "easeOut" }}
              />
              <motion.path
                d={path}
                fill="none"
                stroke={isBlocked ? "url(#battle-route-block)" : "url(#battle-route-attribute)"}
                strokeWidth="9"
                strokeLinecap="round"
                filter="url(#battle-route-glow)"
                initial={reduceMotion ? false : { opacity: 0, pathLength: 0 }}
                animate={{ opacity: 0.3, pathLength: 1 }}
                exit={{ opacity: 0 }}
                transition={{ duration: reduceMotion ? 0 : 0.42, ease: "easeOut" }}
              />
              <motion.path
                d={path}
                fill="none"
                stroke={isBlocked ? "#ecfeff" : "url(#battle-route-attribute)"}
                strokeWidth="2.4"
                strokeLinecap="round"
                initial={reduceMotion ? false : { opacity: 0, pathLength: 0 }}
                animate={{ opacity: 0.94, pathLength: 1 }}
                exit={{ opacity: 0 }}
                transition={{ duration: reduceMotion ? 0 : 0.46, ease: "easeOut" }}
              />

              <AttributeAttackEffect
                attributes={attackTheme.attributes}
                path={path}
                source={points.source}
                target={points.target}
                reduceMotion={shouldReduceMotion}
              />

              <motion.circle
                cx={points.target.x}
                cy={points.target.y}
                fill="none"
                stroke={isBlocked ? "#67e8f9" : attackTheme.accent}
                strokeWidth="2.5"
                initial={reduceMotion ? false : { r: 12, opacity: 1 }}
                animate={{ r: reduceMotion ? 34 : 66, opacity: 0 }}
                transition={{ duration: reduceMotion ? 0 : 0.68, ease: "easeOut" }}
              />
            </motion.g>
          )}
        </AnimatePresence>
      </svg>
    </div>
  );
}
