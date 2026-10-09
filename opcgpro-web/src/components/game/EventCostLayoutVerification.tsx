"use client";

import { useEffect, useState } from "react";
import LayoutPreviewFrame from "@/components/home/LayoutPreviewFrame";
import PromptOverlay from "@/components/game/PromptOverlay";
import { GameRequest } from "@/net/GameRequest";
import { useGameStore, type PromptView } from "@/store/gameStore";

/** 隔离的费用提示验证样本；模拟权威响应，不向真实对局发送请求。 */
export default function EventCostLayoutVerification({ mobile }: { mobile: boolean }) {
  const [ready, setReady] = useState(false);
  const [answer, setAnswer] = useState<string[]>([]);

  useEffect(() => {
    const originalRespond = GameRequest.respondPrompt;
    const cost: PromptView = {
      promptId: "event-cost-2", operationId: "event-cost-2", kind: "RestOwnDon",
      text: "选择 2 张活跃咚转为休息状态", minChoose: 2, maxChoose: 2,
      validChoices: ["don-a", "don-b", "don-c"],
      extra: {
        sourceNumber: "OP14-096",
        donChoices: ["don-a", "don-b", "don-c"].map(id => ({ id, state: "Active" })),
        canReturnToEffectConfirm: true,
        returnChoiceIds: ["__return_to_effect_confirm__:0", "__return_to_effect_confirm__:1"],
      },
    };
    useGameStore.getState().resetGame();
    useGameStore.setState({ pendingPrompt: cost });
    let completion: ReturnType<typeof setTimeout> | undefined;
    GameRequest.respondPrompt = (_promptId, chosen) => {
      setAnswer(chosen);
      completion = setTimeout(() => useGameStore.setState({ pendingPrompt: null }), 600);
      return true;
    };
    // 普通快照重复带回同一操作，已选费用不得被清空或让提示消失。
    const refresh = setInterval(() => {
      if (useGameStore.getState().pendingPrompt?.promptId === cost.promptId)
        useGameStore.setState({ pendingPrompt: { ...cost } });
    }, 200);
    setReady(true);
    return () => {
      clearInterval(refresh);
      clearTimeout(completion);
      GameRequest.respondPrompt = originalRespond;
      useGameStore.getState().resetGame();
    };
  }, []);

  return (
    <LayoutPreviewFrame mode={mobile ? "mobile-landscape" : "desktop"} rotateQuarterTurn={mobile} edgeToEdge>
      <main data-event-cost-verification data-event-cost-answer={JSON.stringify(answer)} className="h-full w-full bg-[#07111f]">
        {ready && <PromptOverlay />}
      </main>
    </LayoutPreviewFrame>
  );
}
