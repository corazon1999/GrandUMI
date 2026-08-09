"use client";

import { AnimatePresence, motion, useReducedMotion } from "framer-motion";
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import CardItem from "@/components/ui/CardItem";
import { getCard, getGameCard } from "@/data/CardLoader";
import { useAudio } from "@/hooks/useAudio";
import { useGameStore, type QueuedEffectActivation } from "@/store/gameStore";

const EFFECT_DURATION_MS = 880;

interface CardBounds {
  left: number;
  top: number;
  width: number;
  height: number;
}

const PARTICLE_OFFSETS = [
  { x: 0, y: -54 },
  { x: 38, y: -38 },
  { x: 54, y: 0 },
  { x: 38, y: 38 },
  { x: 0, y: 54 },
  { x: -38, y: 38 },
  { x: -54, y: 0 },
  { x: -38, y: -38 },
];

const TRIGGER_LABELS: Record<string, string> = {
  OnEnterField: "登场时",
  OnAttackDeclare: "攻击时",
  OnOppAttackDeclare: "对方攻击时",
  OnBlockDeclare: "阻挡时",
  PreKO: "即将被 K.O. 时",
  OnKO: "K.O. 时",
  OnDamageToLeader: "造成伤害时",
  OnLifeRevealTrigger: "触发",
  OnTurnStart: "回合开始时",
  OnMyTurnEnd: "我方回合结束时",
  OnOppTurnEnd: "对方回合结束时",
  OnDonAttached: "赋予咚!!时",
  OnDrawCard: "抽牌时",
  OnPlayCard: "出牌时",
  OnEnterTrash: "进入废弃区时",
  OnDonReturnedToDeck: "咚!!返回卡组时",
  OnCharRested: "角色休息时",
  OnCharLeaveField: "角色离场时",
  OnLifeLeaveField: "生命离场时",
  OnAllyCharEnter: "我方角色登场时",
  OnOppEventPlayed: "对方发动事件时",
  OnOppBlocker: "对方发动阻挡者时",
  OnAllyWillBeKOd: "我方角色将被 K.O. 时",
  OnAllyWillLeaveField: "我方角色将离场时",
  OnAnyCharKOd: "角色被 K.O. 时",
  OnBattleEnd: "战斗结束时",
  OnLeaderBattle: "领袖战斗时",
  OnTriggerActivated: "生命触发发动时",
  OnHandDiscarded: "手牌被丢弃时",
  ActivatedMain: "启动主要",
  EventMain: "主要",
  EventCounter: "反击",
};

function findCardBounds(layer: HTMLElement, sourceId: string): CardBounds | null {
  const stage = layer.parentElement;
  if (!stage) return null;

  const source = Array.from(stage.querySelectorAll<HTMLElement>("[data-battle-card-id]"))
    .find((element) => element.dataset.battleCardId === sourceId);
  if (!source) return null;

  // CardItem 自身承担横置旋转；读取它的实际包围盒，光环才能贴合横置后的外形。
  const surface = source.querySelector<HTMLElement>(".overflow-hidden.rounded-md.border-2") ?? source;
  const layerRect = layer.getBoundingClientRect();
  const surfaceRect = surface.getBoundingClientRect();
  const scaleX = layerRect.width / layer.clientWidth || 1;
  const scaleY = layerRect.height / layer.clientHeight || 1;

  return {
    left: (surfaceRect.left - layerRect.left) / scaleX,
    top: (surfaceRect.top - layerRect.top) / scaleY,
    width: surfaceRect.width / scaleX,
    height: surfaceRect.height / scaleY,
  };
}

function SourceAura({
  activation,
  bounds,
  reduceMotion,
}: {
  activation: QueuedEffectActivation;
  bounds: CardBounds;
  reduceMotion: boolean;
}) {
  const card = getCard(activation.cardNumber);
  const name = card?.name ?? activation.cardNumber;
  const trigger = TRIGGER_LABELS[activation.trigger] ?? "卡牌效果";

  return (
    <motion.div
      key={activation.id}
      className="pointer-events-none absolute z-10"
      style={bounds}
      initial={reduceMotion ? { opacity: 0 } : { opacity: 0, scale: 0.88 }}
      animate={reduceMotion
        ? { opacity: [0, 1, 1, 0] }
        : { opacity: [0, 1, 1, 0], scale: [0.88, 1.1, 1.04, 1] }}
      exit={{ opacity: 0 }}
      transition={{ duration: EFFECT_DURATION_MS / 1000, times: [0, 0.16, 0.72, 1] }}
    >
      <motion.div
        className="absolute -inset-2 rounded-lg border-2 border-amber-100 shadow-[0_0_14px_rgba(251,191,36,.95),0_0_34px_rgba(249,115,22,.75),inset_0_0_16px_rgba(253,224,71,.55)]"
        animate={reduceMotion ? undefined : { rotate: [0, 2, -2, 0] }}
        transition={{ duration: 0.55 }}
      />
      <motion.div
        className="absolute -inset-5 rounded-xl border border-amber-300/80"
        initial={{ opacity: 0.9, scale: 0.72 }}
        animate={reduceMotion ? { opacity: 0.45 } : { opacity: 0, scale: 1.42 }}
        transition={{ duration: reduceMotion ? 0 : 0.68, ease: "easeOut" }}
      />
      <div className="absolute -inset-6 rounded-2xl bg-[radial-gradient(circle,rgba(254,240,138,.42),rgba(249,115,22,.16)_42%,transparent_70%)] mix-blend-screen" />

      {!reduceMotion && PARTICLE_OFFSETS.map((offset, index) => (
        <motion.span
          key={index}
          className="absolute left-1/2 top-1/2 h-1.5 w-1.5 rounded-full bg-amber-100 shadow-[0_0_7px_#fbbf24]"
          initial={{ x: -3, y: -3, opacity: 0, scale: 0.4 }}
          animate={{
            x: offset.x,
            y: offset.y,
            opacity: [0, 1, 0],
            scale: [0.4, 1.25, 0.5],
          }}
          transition={{ duration: 0.62, delay: 0.08 + index * 0.015, ease: "easeOut" }}
        />
      ))}

      <motion.div
        className="absolute bottom-full left-1/2 mb-3 flex -translate-x-1/2 flex-col items-center whitespace-nowrap"
        initial={reduceMotion ? false : { opacity: 0, y: 8, scale: 0.85 }}
        animate={{ opacity: 1, y: 0, scale: 1 }}
        transition={{ duration: reduceMotion ? 0 : 0.18 }}
      >
        <span className="rounded-full border border-amber-100/70 bg-gradient-to-r from-orange-600 via-amber-400 to-orange-600 px-3 py-1 text-[11px] font-black tracking-[0.18em] text-slate-950 shadow-lg shadow-amber-500/40">
          效果发动
        </span>
        <span className="mt-1 max-w-44 truncate rounded bg-slate-950/90 px-2 py-0.5 text-[9px] font-bold text-amber-100 ring-1 ring-amber-300/35">
          {name} · {trigger}
        </span>
      </motion.div>
    </motion.div>
  );
}

function CardCutIn({
  activation,
  reduceMotion,
}: {
  activation: QueuedEffectActivation;
  reduceMotion: boolean;
}) {
  const spriteMap = useGameStore((state) =>
    activation.side === "my" ? state.my?.spriteMap : state.opponent?.spriteMap,
  );
  const card = getGameCard(activation.cardNumber, spriteMap) ?? null;
  const name = card?.name ?? activation.cardNumber;
  const trigger = TRIGGER_LABELS[activation.trigger] ?? "卡牌效果";

  return (
    <motion.div
      key={activation.id}
      className="pointer-events-none absolute left-1/2 top-1/2 z-20 flex -translate-x-1/2 -translate-y-1/2 items-center gap-4 rounded-2xl border border-amber-200/60 bg-slate-950/95 p-3 pr-6 shadow-[0_0_24px_rgba(251,191,36,.48),0_18px_55px_rgba(0,0,0,.7)] backdrop-blur"
      initial={reduceMotion ? { opacity: 0 } : { opacity: 0, x: 32, scale: 0.82 }}
      animate={reduceMotion
        ? { opacity: [0, 1, 1, 0] }
        : { opacity: [0, 1, 1, 0], x: [32, 0, 0, -12], scale: [0.82, 1.04, 1, 0.97] }}
      exit={{ opacity: 0 }}
      transition={{ duration: EFFECT_DURATION_MS / 1000, times: [0, 0.18, 0.76, 1] }}
    >
      <div className="relative rounded-lg shadow-[0_0_18px_rgba(251,191,36,.7)]">
        <CardItem card={card} size="md" />
        <motion.div
          className="absolute -inset-2 rounded-lg border-2 border-amber-200"
          animate={reduceMotion ? undefined : { opacity: [0.25, 1, 0.25], scale: [0.96, 1.06, 0.96] }}
          transition={{ duration: 0.58, repeat: 1 }}
        />
      </div>
      <div className="min-w-44">
        <p className="text-[10px] font-black tracking-[0.25em] text-amber-300">
          {activation.side === "my" ? "我方" : "对手"} · EFFECT
        </p>
        <p className="mt-1 text-xl font-black text-white">效果发动</p>
        <p className="mt-2 max-w-56 truncate text-sm font-bold text-amber-100">{name}</p>
        <p className="mt-1 text-xs font-bold text-orange-300">【{trigger}】</p>
      </div>
    </motion.div>
  );
}

/**
 * 顺序播放服务端下发的卡牌效果发动事件：场上来源贴牌高亮，已离场或事件卡使用卡图切入。
 * 整层不接管鼠标事件，动画期间仍可正常操作牌桌与 Prompt。
 */
export default function EffectActivationLayer() {
  const layerRef = useRef<HTMLDivElement>(null);
  const queued = useGameStore((state) => state.effectActivationQueue[0] ?? null);
  const shiftEffectActivation = useGameStore((state) => state.shiftEffectActivation);
  const tick = useGameStore((state) => state.tick);
  const { play } = useAudio();
  const reduceMotion = useReducedMotion() ?? false;
  const [active, setActive] = useState<QueuedEffectActivation | null>(null);
  // undefined=尚未测量，null=已确认来源不在牌桌上。
  const [bounds, setBounds] = useState<CardBounds | null | undefined>(undefined);

  useEffect(() => {
    if (active || !queued) return;
    shiftEffectActivation();
    setBounds(undefined);
    setActive(queued);
  }, [active, queued, shiftEffectActivation]);

  useEffect(() => {
    if (!active) return;
    play("effect");
    const timeout = window.setTimeout(() => setActive(null), EFFECT_DURATION_MS);
    return () => window.clearTimeout(timeout);
  }, [active, play]);

  useLayoutEffect(() => {
    const layer = layerRef.current;
    if (!layer || !active) {
      setBounds(undefined);
      return;
    }

    let frame = 0;
    const update = () => setBounds(findCardBounds(layer, active.sourceId));
    const scheduleUpdate = () => {
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(update);
    };

    update();
    window.addEventListener("resize", scheduleUpdate);
    const observer = new ResizeObserver(scheduleUpdate);
    observer.observe(layer);
    return () => {
      cancelAnimationFrame(frame);
      window.removeEventListener("resize", scheduleUpdate);
      observer.disconnect();
    };
  }, [active, tick]);

  const statusText = useMemo(() => {
    if (!active) return "";
    const name = getCard(active.cardNumber)?.name ?? active.cardNumber;
    return `${active.side === "my" ? "我方" : "对手"}${name}发动了效果`;
  }, [active]);

  return (
    <div
      ref={layerRef}
      className="pointer-events-none absolute inset-0 z-40"
      role="status"
      aria-live="polite"
      aria-label={statusText}
    >
      <AnimatePresence mode="wait">
        {active && bounds
          ? <SourceAura activation={active} bounds={bounds} reduceMotion={reduceMotion} />
          : active && bounds === null
            ? <CardCutIn activation={active} reduceMotion={reduceMotion} />
            : null}
      </AnimatePresence>
    </div>
  );
}
