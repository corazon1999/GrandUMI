"use client";

import { motion, AnimatePresence } from "framer-motion";
import { useState, useEffect } from "react";
import { useGameStore } from "@/store/gameStore";
import { GameRequest } from "@/net/GameRequest";
import { getCard } from "@/data/CardLoader";
import CardItem from "@/components/ui/CardItem";

/**
 * 服务端 Prompt 弹窗：处理选择目标 / 选项 / 生命牌触发等交互
 */
export default function PromptOverlay() {
  const prompt = useGameStore((s) => s.pendingPrompt);
  const my = useGameStore((s) => s.my);
  const opp = useGameStore((s) => s.opponent);
  const [selected, setSelected] = useState<string[]>([]);

  useEffect(() => { setSelected([]); }, [prompt?.promptId]);

  if (!prompt) return null;

  const isLifeTrigger = prompt.kind === "LifeTrigger";
  const isOption = prompt.kind === "Option";
  const options = prompt.extra?.options as string[] | undefined;
  const lifeCardNumber = prompt.extra?.lifeCardNumber as string | undefined;
  const lifeCard = lifeCardNumber ? getCard(lifeCardNumber) ?? null : null;
  const hasRealTrigger = prompt.extra?.hasRealTrigger === true;

  // 把 cardId 反查成显示用 CardData（自己 / 对手场上）
  const findCardById = (id: string) => {
    if (id === "leader") return null;
    const allCards = [
      ...(my?.fieldCards ?? []),
      ...(opp?.fieldCards ?? []),
    ];
    const found = allCards.find((c) => c.id === id);
    return found ? getCard(found.number) ?? null : null;
  };

  const toggle = (id: string) => {
    setSelected((prev) => {
      if (prev.includes(id)) return prev.filter((x) => x !== id);
      if (prev.length >= prompt.maxChoose) return [id]; // 单选时替换
      return [...prev, id];
    });
  };

  const canConfirm = selected.length >= prompt.minChoose && selected.length <= prompt.maxChoose;

  const handleConfirm = () => {
    GameRequest.respondPrompt(prompt.promptId, selected);
  };
  const handleSkip = () => {
    GameRequest.respondPrompt(prompt.promptId, []);
  };

  return (
    <AnimatePresence>
      <motion.div
        className="fixed inset-0 z-50 bg-black/75 flex flex-col items-center justify-center gap-6"
        initial={{ opacity: 0 }} animate={{ opacity: 1 }}
      >
        <p className="text-white text-lg font-bold">{prompt.text}</p>

        {isLifeTrigger && (
          <div className="flex flex-col items-center gap-4">
            {lifeCard && hasRealTrigger && <CardItem card={lifeCard} size="lg" />}
            {!hasRealTrigger && (
              <div className="w-28 h-40 rounded-lg bg-gradient-to-br from-blue-900 to-blue-950 flex items-center justify-center">
                <span className="text-blue-400 text-xs font-bold">??</span>
              </div>
            )}
            <div className="flex gap-3">
              <button
                onClick={() => GameRequest.respondPrompt(prompt.promptId, ["trigger"])}
                className="bg-orange-500 hover:bg-orange-400 text-white px-6 py-2 rounded-lg font-bold"
              >
                发动触发
              </button>
              <button
                onClick={() => GameRequest.respondPrompt(prompt.promptId, ["hand"])}
                className="bg-gray-600 hover:bg-gray-500 text-white px-6 py-2 rounded-lg font-bold"
              >
                加入手牌
              </button>
            </div>
          </div>
        )}

        {isOption && options && (
          <div className="flex flex-col gap-2">
            {options.map((opt, i) => (
              <button key={i}
                onClick={() => GameRequest.respondPrompt(prompt.promptId, [i.toString()])}
                className="bg-blue-600 hover:bg-blue-500 text-white px-6 py-2 rounded-lg">
                {opt}
              </button>
            ))}
          </div>
        )}

        {!isLifeTrigger && !isOption && (
          <>
            <div className="flex flex-wrap gap-2 max-w-2xl justify-center">
              {prompt.validChoices.map((id) => {
                const card = findCardById(id);
                return (
                  <div key={id} onClick={() => toggle(id)} className="cursor-pointer">
                    <CardItem
                      card={card}
                      size="md"
                      isSelected={selected.includes(id)}
                    />
                  </div>
                );
              })}
              {prompt.validChoices.length === 0 && (
                <span className="text-gray-400 text-sm">无可选目标</span>
              )}
            </div>

            <div className="flex gap-3">
              {prompt.minChoose === 0 && (
                <button onClick={handleSkip}
                  className="bg-gray-600 hover:bg-gray-500 text-white px-6 py-2 rounded-lg">
                  跳过
                </button>
              )}
              <button onClick={handleConfirm} disabled={!canConfirm}
                className="bg-orange-500 hover:bg-orange-400 disabled:bg-gray-700 disabled:cursor-not-allowed text-white px-6 py-2 rounded-lg font-bold">
                确认（已选 {selected.length} / {prompt.maxChoose}）
              </button>
            </div>
          </>
        )}
      </motion.div>
    </AnimatePresence>
  );
}
