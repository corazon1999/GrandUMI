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

  // 服务端注入的效果源卡号：让玩家知道当前在结算哪张卡的效果
  const sourceNumber = prompt.extra?.sourceNumber as string | undefined;
  const sourceCard = sourceNumber ? getCard(sourceNumber) ?? null : null;
  const options = prompt.extra?.options as string[] | undefined;
  const lifeCardNumber = prompt.extra?.lifeCardNumber as string | undefined;
  const lifeCard = lifeCardNumber ? getCard(lifeCardNumber) ?? null : null;
  const hasRealTrigger = prompt.extra?.hasRealTrigger === true;

  // 观星 / 卡组重排等"自选顺序"提示：按点选先后决定相对顺序，
  // 给已选卡叠加"第N张"徽标，防止玩家忘记自己点选的顺序。
  const isOrdered = prompt.text?.includes("自选顺序") ?? false;

  // 服务端可在 extra.choiceCards 里携带候选卡的 {id, number}，
  // 用于显示卡组/手牌等"不下发身份"区域的候选（findCardById 默认只认场上卡）
  const choiceCards = (prompt.extra?.choiceCards as { id: string; number: string }[] | undefined) ?? [];
  const choiceMap = new Map(choiceCards.map((c) => [c.id, c.number]));

  // 检索/确认类效果约定：choiceCards = "确认到的全部牌"（让玩家看全），
  // validChoices = "可公开/可选的子集"。展示全部 choiceCards（叠加不在其中的 validChoices），
  // 仅 validChoices 中的卡可点选，其余置灰仅供确认。
  const validChoiceSet = new Set(prompt.validChoices);
  const displayChoiceIds =
    choiceCards.length > 0
      ? [...choiceCards.map((c) => c.id), ...prompt.validChoices.filter((id) => !choiceMap.has(id))]
      : prompt.validChoices;

  // 把 cardId 反查成显示用 CardData（优先 extra.choiceCards，再领袖，最后退回自己/对手场上）
  const findCardById = (id: string) => {
    const numFromExtra = choiceMap.get(id);
    if (numFromExtra) return getCard(numFromExtra) ?? null;
    // 领袖不在 fieldCards 里，需单独识别（候选 id 为领袖 GUID 或字面 "leader"），否则卡图无法加载
    if (id === "leader" || id === my?.leaderId)
      return my?.leaderNumber ? getCard(my.leaderNumber) ?? null : null;
    if (id === opp?.leaderId)
      return opp?.leaderNumber ? getCard(opp.leaderNumber) ?? null : null;
    const allCards = [
      ...(my?.fieldCards ?? []),
      ...(opp?.fieldCards ?? []),
    ];
    const found = allCards.find((c) => c.id === id);
    return found ? getCard(found.number) ?? null : null;
  };

  // 把候选 id 反查成场上真实状态（贴咚数 / 当前攻击力修正），让选择列表与牌桌同步。
  // powerBuff 取「服务端当前攻击力 - 基础power - 贴咚*1000」，使 CardItem 的 displayPower 等于权威当前值。
  // 卡组/手牌等非场上候选返回 null，CardItem 退回基础卡面。
  const fieldStateById = (
    id: string,
  ): { attachedDonCount: number; powerBuff: number; isTapped: boolean } | null => {
    if (my && (id === "leader" || id === my.leaderId)) {
      const base = my.leaderNumber ? getCard(my.leaderNumber)?.power ?? 0 : 0;
      return {
        attachedDonCount: my.leaderAttachedDon,
        powerBuff: my.leaderPower - base - my.leaderAttachedDon * 1000,
        isTapped: my.leaderTapped,
      };
    }
    if (opp && id === opp.leaderId) {
      const base = opp.leaderNumber ? getCard(opp.leaderNumber)?.power ?? 0 : 0;
      return {
        attachedDonCount: opp.leaderAttachedDon,
        powerBuff: opp.leaderPower - base - opp.leaderAttachedDon * 1000,
        isTapped: opp.leaderTapped,
      };
    }
    const fc = [...(my?.fieldCards ?? []), ...(opp?.fieldCards ?? [])].find((c) => c.id === id);
    if (!fc) return null;
    const base = getCard(fc.number)?.power ?? 0;
    return {
      attachedDonCount: fc.attachedDon,
      powerBuff: fc.powerCurrent - base - fc.attachedDon * 1000,
      isTapped: fc.isTapped,
    };
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
        className="fixed inset-0 z-50 overflow-y-auto bg-black/75"
        initial={{ opacity: 0 }} animate={{ opacity: 1 }}
      >
        {/* 内容包一层：内容少时居中；横屏等矮视口下内容超高时可纵向滚动、不裁切底部「加入手牌/确认」按钮 */}
        <div className="flex min-h-full flex-col items-center justify-center gap-6 p-4 max-md:justify-start max-md:gap-4">
        {sourceCard && (
          <div className="flex flex-col items-center gap-1">
            <span className="text-amber-300 text-xs font-bold tracking-wide">效果源</span>
            <CardItem card={sourceCard} size="sm" />
          </div>
        )}

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
              {displayChoiceIds.map((id) => {
                const card = findCardById(id);
                const fieldState = fieldStateById(id);
                const selectable = validChoiceSet.has(id);
                const orderIdx = isOrdered ? selected.indexOf(id) : -1;
                return (
                  <div
                    key={id}
                    onClick={selectable ? () => toggle(id) : undefined}
                    className={`relative ${selectable ? "cursor-pointer" : "cursor-default"}`}
                    title={selectable ? undefined : "仅供确认，不可选择"}
                  >
                    {/* 不满足条件的牌照常显示卡图，仅不可点选，用角标提示 */}
                    <CardItem
                      card={card}
                      size="md"
                      isSelected={selectable && selected.includes(id)}
                      attachedDonCount={fieldState?.attachedDonCount ?? 0}
                      powerBuff={fieldState?.powerBuff ?? 0}
                    />
                    {!selectable && (
                      <span className="pointer-events-none absolute inset-x-0 bottom-0 z-20 bg-black/65 py-0.5 text-center text-[10px] font-bold text-slate-200">
                        不可选
                      </span>
                    )}
                    {orderIdx >= 0 && (
                      <span className="pointer-events-none absolute -top-1.5 -left-1.5 z-30 flex h-6 min-w-6 items-center justify-center rounded-full bg-amber-500 px-1.5 text-[11px] font-bold text-black shadow-md ring-2 ring-white">
                        第{orderIdx + 1}张
                      </span>
                    )}
                  </div>
                );
              })}
              {displayChoiceIds.length === 0 && (
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
        </div>
      </motion.div>
    </AnimatePresence>
  );
}
