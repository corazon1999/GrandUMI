"use client";

import { useEffect, useRef, useState } from "react";
import { useGameStore } from "@/store/gameStore";

/**
 * 动画事件类型 — 由 lastAction 解析得到
 * 组件可监听对应事件播放 Framer Motion / CSS 动画
 */
export type AnimationEvent =
  | { type: "none" }
  | { type: "drawCard"; side: "my" | "opponent"; cardNumber?: string }
  | { type: "playCard"; side: "my" | "opponent"; cardNumber: string; fieldIndex?: number }
  | { type: "attack"; attackerId: string; targetIsLeader: boolean; targetId: string | null }
  | { type: "block"; blockerIndex: number }
  | { type: "counter"; handIndex: number }
  | { type: "damage"; target: "leader" | "character"; success: boolean }
  | { type: "koUnit"; side: "my" | "opponent"; cardIndex: number }
  | { type: "turnStart"; side: "my" | "opponent" }
  | { type: "turnEnd" }
  | { type: "gameOver"; isWin: boolean };

/**
 * useGameAnimation — 监听服务端推送的 lastAction，解析为动画事件
 *
 * 用法：
 *   const anim = useGameAnimation();
 *   useEffect(() => { if (anim.type === "attack") playAttackAnim(); }, [anim]);
 */
export function useGameAnimation(): AnimationEvent {
  const lastAction = useGameStore((s) => s.lastAction);
  const lastActionPayload = useGameStore((s) => s.lastActionPayloadObj);
  const tick = useGameStore((s) => s.tick);
  const [event, setEvent] = useState<AnimationEvent>({ type: "none" });
  const prevTickRef = useRef<number>(-1);

  useEffect(() => {
    if (!lastAction || tick === prevTickRef.current) return;
    prevTickRef.current = tick;

    const payload = lastActionPayload ?? {};

    switch (lastAction) {
      case "DrawCard":
        setEvent({ type: "drawCard", side: "my", cardNumber: payload.cardNumber as string });
        break;
      case "PlayCard":
        setEvent({
          type: "playCard",
          side: "my",
          cardNumber: (payload.cardNumber as string) ?? "",
          fieldIndex: payload.fieldIndex as number | undefined,
        });
        break;
      case "Attack":
        setEvent({
          type: "attack",
          attackerId: (payload.attacker as string) ?? "",
          targetIsLeader: (payload.targetIsLeader as boolean) ?? false,
          targetId: (payload.targetId as string) ?? null,
        });
        break;
      case "Block":
        setEvent({ type: "block", blockerIndex: (payload.blockerIndex as number) ?? 0 });
        break;
      case "Counter":
        setEvent({ type: "counter", handIndex: (payload.handIndex as number) ?? 0 });
        break;
      case "Damage":
        setEvent({
          type: "damage",
          target: (payload.target as "leader" | "character") ?? "leader",
          success: (payload.success as boolean) ?? true,
        });
        break;
      case "KOUnit":
        setEvent({
          type: "koUnit",
          side: (payload.side as "my" | "opponent") ?? "opponent",
          cardIndex: (payload.cardIndex as number) ?? 0,
        });
        break;
      case "TurnStart":
        setEvent({ type: "turnStart", side: payload.currentTurn ? "my" : "opponent" });
        break;
      case "TurnEnd":
        setEvent({ type: "turnEnd" });
        break;
      case "GameOver":
        setEvent({ type: "gameOver", isWin: (payload.isWin as boolean) ?? false });
        break;
      default:
        setEvent({ type: "none" });
    }
  }, [tick, lastAction, lastActionPayload]);

  return event;
}
