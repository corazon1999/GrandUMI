import { ref, watch, type Ref } from "vue";
import { useStore } from "./useStore";
import { useGameStore } from "@/store/gameStore";

/** 动画事件类型 — 由 lastAction 解析得到 */
export type AnimationEvent =
  | { type: "none" }
  | { type: "drawCard"; side: "my" | "opponent"; cardNumber?: string }
  | { type: "playCard"; side: "my" | "opponent"; cardNumber: string; fieldIndex?: number }
  | { type: "attack"; attackerIndex: number; targetIndex: number | "leader" }
  | { type: "block"; blockerIndex: number }
  | { type: "counter"; handIndex: number }
  | { type: "damage"; target: "leader" | "character"; success: boolean }
  | { type: "koUnit"; side: "my" | "opponent"; cardIndex: number }
  | { type: "turnStart"; side: "my" | "opponent" }
  | { type: "turnEnd" }
  | { type: "gameOver"; isWin: boolean };

/**
 * useGameAnimation — 监听服务端推送的 lastAction，解析为动画事件。
 * 返回一个响应式 ref；组件可 watch 它来播放动画。
 */
export function useGameAnimation(): Ref<AnimationEvent> {
  const lastAction = useStore(useGameStore, (s) => s.lastAction);
  const lastActionPayload = useStore(useGameStore, (s) => s.lastActionPayloadObj);
  const event = ref<AnimationEvent>({ type: "none" });
  let prevAction = "";

  watch(
    [lastAction, lastActionPayload],
    () => {
      const action = lastAction.value;
      if (!action || action === prevAction) return;
      prevAction = action;
      const payload = lastActionPayload.value ?? {};

      switch (action) {
        case "DrawCard":
          event.value = { type: "drawCard", side: "my", cardNumber: payload.cardNumber as string };
          break;
        case "PlayCard":
          event.value = {
            type: "playCard",
            side: "my",
            cardNumber: (payload.cardNumber as string) ?? "",
            fieldIndex: payload.fieldIndex as number | undefined,
          };
          break;
        case "Attack":
          event.value = {
            type: "attack",
            attackerIndex: (payload.attackerIndex as number) ?? 0,
            targetIndex: (payload.targetIndex as number | "leader") ?? 0,
          };
          break;
        case "Block":
          event.value = { type: "block", blockerIndex: (payload.blockerIndex as number) ?? 0 };
          break;
        case "Counter":
          event.value = { type: "counter", handIndex: (payload.handIndex as number) ?? 0 };
          break;
        case "Damage":
          event.value = {
            type: "damage",
            target: (payload.target as "leader" | "character") ?? "leader",
            success: (payload.success as boolean) ?? true,
          };
          break;
        case "KOUnit":
          event.value = {
            type: "koUnit",
            side: (payload.side as "my" | "opponent") ?? "opponent",
            cardIndex: (payload.cardIndex as number) ?? 0,
          };
          break;
        case "TurnStart":
          event.value = { type: "turnStart", side: payload.currentTurn ? "my" : "opponent" };
          break;
        case "TurnEnd":
          event.value = { type: "turnEnd" };
          break;
        case "GameOver":
          event.value = { type: "gameOver", isWin: (payload.isWin as boolean) ?? false };
          break;
        default:
          event.value = { type: "none" };
      }
    },
    { immediate: true },
  );

  return event;
}
