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
  const serverPrompt = useGameStore((s) => s.pendingPrompt);
  const localOverflowHandIndex = useGameStore((s) => s.localOverflowHandIndex);
  const my = useGameStore((s) => s.my);
  const opp = useGameStore((s) => s.opponent);
  const flashPromptSuccess = useGameStore((s) => s.flashPromptSuccess);
  const clearLocalOverflow = useGameStore((s) => s.clearLocalOverflow);
  const [selected, setSelected] = useState<string[]>([]);
  const [submittingPromptId, setSubmittingPromptId] = useState<string | null>(null);
  const [isMinimized, setIsMinimized] = useState(false);

  const localPrompt: typeof serverPrompt = localOverflowHandIndex !== null && my
    ? {
        promptId: `local-overflow-${localOverflowHandIndex}`,
        kind: "LocalOverflowTrash",
        text: "角色区已满，请选择 1 张角色送去废弃区",
        validChoices: my.fieldCards.map((c) => c.id),
        minChoose: 1,
        maxChoose: 1,
        extra: {},
      }
    : null;
  const prompt = serverPrompt ?? localPrompt;

  useEffect(() => {
    setSelected([]);
    setSubmittingPromptId(null);
    setIsMinimized(false);
  }, [prompt?.promptId]);

  // 网络异常时允许重新提交，避免弹窗永久消失。
  useEffect(() => {
    if (!submittingPromptId) return;
    const timer = window.setTimeout(() => setSubmittingPromptId(null), 3000);
    return () => window.clearTimeout(timer);
  }, [submittingPromptId]);

  if (!prompt || submittingPromptId === prompt.promptId) return null;

  if (isMinimized) {
    return (
      <AnimatePresence>
        <motion.div
          className="pointer-events-none fixed inset-0 z-[60]"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
        >
          <button
            type="button"
            onClick={() => setIsMinimized(false)}
            className="pointer-events-auto fixed bottom-4 right-4 rounded-lg bg-slate-800 px-4 py-2 text-sm font-bold text-white shadow-lg ring-1 ring-white/30 hover:bg-slate-700"
          >
            恢复选择
          </button>
        </motion.div>
      </AnimatePresence>
    );
  }

  const isLifeTrigger = prompt.kind === "LifeTrigger";
  const isOption = prompt.kind === "Option";
  // 「咚!!-N」放回：服务端用 extra.donChoices 携带费用区每张咚的状态与附着目标，
  // 这里独立渲染咚 token（咚牌无卡号、不在 fieldCards，走不了通用卡图反查）。
  const isReturnDon = prompt.kind === "ReturnOwnDon";
  type DonChoice = { id: string; state: string; attachedToNumber?: string; attachedToName?: string };
  const donChoices = (prompt.extra?.donChoices as DonChoice[] | undefined) ?? [];
  const canCancelReturnDon = prompt.extra?.canCancel === true;
  // 通用选择分支里，凡 id 命中 donChoices 的渲染成咚 token（混合"卡牌 + 咚"同列，如 OP16-033 休置成本）
  const donChoiceMap = new Map(donChoices.map((d) => [d.id, d]));

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
  const choiceCards = (prompt.extra?.choiceCards as { id: string; number: string; zone?: string }[] | undefined) ?? [];
  const choiceMap = new Map(choiceCards.map((c) => [c.id, c.number]));
  // 服务端按候选 id 补充区域；混合手牌/墓地选择时为同名卡提供明确来源标识。
  const choiceCardZones = (prompt.extra?.choiceCardZones as { id: string; zone: string }[] | undefined) ?? [];
  const choiceZoneMap = new Map([
    ...choiceCards.filter((c) => c.zone).map((c) => [c.id, c.zone!] as const),
    ...choiceCardZones.map((c) => [c.id, c.zone] as const),
  ]);

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
    // 舞台不在 fieldCards 里（stageId/stageNumber 扁平字段），需单独识别，否则候选卡图加载不出
    if (my && id === my.stageId)
      return my.stageNumber ? getCard(my.stageNumber) ?? null : null;
    if (opp && id === opp.stageId)
      return opp.stageNumber ? getCard(opp.stageNumber) ?? null : null;
    const allCards = [
      ...(my?.fieldCards ?? []),
      ...(opp?.fieldCards ?? []),
    ];
    const found = allCards.find((c) => c.id === id);
    return found ? getCard(found.number) ?? null : null;
  };

  // 场上目标需要明确所属阵营，避免双方存在同名或同卡图角色时无法分辨。
  // 手牌、卡组、废弃区等非场上候选不显示阵营标记。
  const fieldSideById = (id: string): "my" | "opponent" | null => {
    if (
      my &&
      (id === "leader" ||
        id === my.leaderId ||
        id === my.stageId ||
        my.fieldCards.some((c) => c.id === id))
    ) {
      return "my";
    }
    if (
      opp &&
      (id === opp.leaderId ||
        id === opp.stageId ||
        opp.fieldCards.some((c) => c.id === id))
    ) {
      return "opponent";
    }
    return null;
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
    // 舞台：无贴咚、无力量修正，仅横置状态
    if (my && id === my.stageId)
      return { attachedDonCount: 0, powerBuff: 0, isTapped: my.stageTapped };
    if (opp && id === opp.stageId)
      return { attachedDonCount: 0, powerBuff: 0, isTapped: opp.stageTapped };
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

  // ReturnOwnDon 的 0 张仅表示“不发动”；点击“确认放回”仍必须选满指定数量。
  const canConfirm = isReturnDon
    ? selected.length === prompt.maxChoose
    : selected.length >= prompt.minChoose && selected.length <= prompt.maxChoose;

  const submitServerPrompt = (chosen: string[], showSuccess = false) => {
    if (submittingPromptId === prompt.promptId) return false;
    const sent = GameRequest.respondPrompt(prompt.promptId, chosen);
    if (!sent) return false;
    setSubmittingPromptId(prompt.promptId);
    if (showSuccess) flashPromptSuccess();
    return true;
  };

  const handleConfirm = () => {
    const victimId = selected[0];
    const isLocalOverflow = prompt.kind === "LocalOverflowTrash";
    if (!isLocalOverflow) {
      submitServerPrompt(selected, true);
      return;
    }

    const sent = GameRequest.playCard(localOverflowHandIndex!, victimId);
    if (!sent) return;

    // 立即收起弹窗；场上角色和废弃区只等待服务端权威快照更新，
    // 避免本地提前移牌与后续效果/拒绝响应叠加时出现视觉错位。
    setSubmittingPromptId(prompt.promptId);
    clearLocalOverflow();
    // #241 目标确认后弹一个"选择成功"瞬时提示（弹窗随即由服务器快照关闭）
    flashPromptSuccess();
  };
  const handleSkip = () => {
    submitServerPrompt([]);
  };
  const handleCancelReturnDon = () => {
    submitServerPrompt([]);
  };

  return (
    <AnimatePresence>
      <motion.div
        className="fixed inset-0 z-50 overflow-y-auto bg-black/75"
        initial={{ opacity: 0 }} animate={{ opacity: 1 }}
      >
        <button
          type="button"
          onClick={() => setIsMinimized(true)}
          className="fixed right-4 top-4 z-[60] rounded-lg bg-slate-800 px-4 py-2 text-sm font-bold text-white shadow-lg ring-1 ring-white/30 hover:bg-slate-700"
        >
          隐藏
        </button>
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
                onClick={() => submitServerPrompt(["trigger"])}
                className="bg-orange-500 hover:bg-orange-400 text-white px-6 py-2 rounded-lg font-bold"
              >
                发动触发
              </button>
              <button
                onClick={() => submitServerPrompt(["hand"])}
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
                onClick={() => submitServerPrompt([i.toString()])}
                className="bg-blue-600 hover:bg-blue-500 text-white px-6 py-2 rounded-lg">
                {opt}
              </button>
            ))}
          </div>
        )}

        {isReturnDon && (
          <>
            <div className="flex flex-wrap gap-3 max-w-2xl justify-center">
              {donChoices.map((d) => {
                const isSel = selected.includes(d.id);
                const attachedCard = d.attachedToNumber ? getCard(d.attachedToNumber) ?? null : null;
                const stateLabel = d.state === "Active" ? "活跃" : d.state === "Rest" ? "休息" : "附着";
                return (
                  <div
                    key={d.id}
                    onClick={() => toggle(d.id)}
                    className={`relative flex w-20 cursor-pointer flex-col items-center gap-1 rounded-lg border-2 p-1.5 transition ${
                      isSel ? "border-orange-400 bg-orange-400/20" : "border-white/20 bg-black/40 hover:border-white/50"
                    }`}
                  >
                    <div
                      className={`flex h-12 w-12 items-center justify-center rounded-full text-[10px] font-black shadow ${
                        d.state === "Rest"
                          ? "rotate-90 bg-gradient-to-br from-indigo-400 to-indigo-700 text-indigo-50"
                          : d.state === "Attached"
                            ? "bg-gradient-to-br from-amber-300 to-amber-600 text-black"
                            : "bg-gradient-to-br from-yellow-300 to-amber-500 text-black"
                      }`}
                    >
                      DON!!
                    </div>
                    <span
                      className={`text-[10px] font-bold ${
                        d.state === "Rest" ? "text-indigo-200" : "text-yellow-100"
                      }`}
                    >
                      {stateLabel}
                    </span>
                    {attachedCard ? (
                      <span
                        className="max-w-full truncate text-[9px] text-amber-200"
                        title={`贴在 ${d.attachedToName ?? attachedCard.name}`}
                      >
                        贴：{d.attachedToName ?? attachedCard.name}
                      </span>
                    ) : (
                      d.state === "Attached" && (
                        <span className="text-[9px] text-amber-200">附着中</span>
                      )
                    )}
                    {isSel && (
                      <span className="absolute -top-1.5 -right-1.5 z-10 flex h-5 w-5 items-center justify-center rounded-full bg-orange-500 text-[11px] font-bold text-white ring-2 ring-white">
                        ✓
                      </span>
                    )}
                  </div>
                );
              })}
              {donChoices.length === 0 && <span className="text-gray-400 text-sm">无可放回的咚</span>}
            </div>
            <div className="flex gap-3">
              {canCancelReturnDon && (
                <button
                  onClick={handleCancelReturnDon}
                  className="bg-gray-600 hover:bg-gray-500 text-white px-6 py-2 rounded-lg"
                >
                  不发动
                </button>
              )}
              <button
                onClick={handleConfirm}
                disabled={!canConfirm}
                className="bg-orange-500 hover:bg-orange-400 disabled:bg-gray-700 disabled:cursor-not-allowed text-white px-6 py-2 rounded-lg font-bold"
              >
                确认放回（{selected.length} / {prompt.maxChoose}）
              </button>
            </div>
          </>
        )}

        {!isLifeTrigger && !isOption && !isReturnDon && (
          <>
            <div className="flex flex-wrap gap-2 max-w-2xl justify-center">
              {displayChoiceIds.map((id) => {
                const selectable = validChoiceSet.has(id);
                // 咚候选：渲染成咚 token，与卡牌同列混选（如 OP16-033「将我方卡牌转为休息」可选活跃咚）
                const don = donChoiceMap.get(id);
                if (don) {
                  const isSel = selectable && selected.includes(id);
                  const stLabel = don.state === "Active" ? "活跃" : don.state === "Rest" ? "休息" : "附着";
                  return (
                    <div
                      key={id}
                      onClick={selectable ? () => toggle(id) : undefined}
                      className={`relative flex h-28 w-20 flex-col items-center justify-center gap-1 rounded-lg border-2 p-1.5 transition ${
                        isSel ? "border-orange-400 bg-orange-400/20" : "border-white/20 bg-black/40"
                      } ${selectable ? "cursor-pointer hover:border-white/50" : "cursor-default opacity-60"}`}
                    >
                      <div
                        className={`flex h-12 w-12 items-center justify-center rounded-full bg-gradient-to-br from-yellow-300 to-amber-500 text-[10px] font-black text-black shadow ${
                          don.state === "Rest" ? "rotate-90" : ""
                        }`}
                      >
                        DON!!
                      </div>
                      <span className="text-[10px] font-bold text-yellow-100">{stLabel}咚</span>
                      {isSel && (
                        <span className="absolute -right-1.5 -top-1.5 z-10 flex h-5 w-5 items-center justify-center rounded-full bg-orange-500 text-[11px] font-bold text-white ring-2 ring-white">
                          ✓
                        </span>
                      )}
                    </div>
                  );
                }
                const card = findCardById(id);
                const fieldState = fieldStateById(id);
                const fieldSide = fieldSideById(id);
                const choiceZone = choiceZoneMap.get(id);
                const zoneLabel = choiceZone === "hand" ? "手牌" : choiceZone === "trash" ? "墓地" : null;
                const fieldIndex = fieldSide === "my"
                  ? (my?.fieldCards.findIndex((c) => c.id === id) ?? -1)
                  : fieldSide === "opponent"
                    ? (opp?.fieldCards.findIndex((c) => c.id === id) ?? -1)
                    : -1;
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
                      isTapped={fieldState?.isTapped ?? false}
                      attachedDonCount={fieldState?.attachedDonCount ?? 0}
                      powerBuff={fieldState?.powerBuff ?? 0}
                    />
                    {fieldSide && (
                      <span
                        className={`pointer-events-none absolute -top-4 left-1/2 z-40 -translate-x-1/2 whitespace-nowrap rounded-full px-2 py-0.5 text-[10px] font-bold text-white shadow-md ring-1 ring-white/70 ${
                          fieldSide === "my" ? "bg-sky-600" : "bg-rose-600"
                        }`}
                      >
                        {fieldSide === "my" ? "己方" : "对方"}
                        {fieldIndex >= 0 ? ` · 第${fieldIndex + 1}位` : ""}
                      </span>
                    )}
                    {zoneLabel && (
                      <span className="pointer-events-none absolute -top-3 -right-1 z-40 rounded-full bg-violet-700 px-2 py-0.5 text-[10px] font-bold text-white shadow-md ring-1 ring-white/70">
                        {zoneLabel}
                      </span>
                    )}
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
