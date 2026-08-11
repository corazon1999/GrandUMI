"use client";

import { motion, AnimatePresence } from "framer-motion";
import { useState, useEffect } from "react";
import { useGameStore } from "@/store/gameStore";
import { GameRequest } from "@/net/GameRequest";
import { getCard, getGameCard } from "@/data/CardLoader";
import CardItem from "@/components/ui/CardItem";

function PromptChevron({ expanded }: { expanded: boolean }) {
  return (
    <svg
      viewBox="0 0 24 24"
      aria-hidden="true"
      className={`h-5 w-5 transition-transform duration-200 ${expanded ? "rotate-180" : ""}`}
    >
      <path
        d="m6.75 14.25 5.25-5.25 5.25 5.25"
        fill="none"
        stroke="currentColor"
        strokeLinecap="round"
        strokeLinejoin="round"
        strokeWidth="2.4"
      />
    </svg>
  );
}

/**
 * 服务端 Prompt 弹窗：处理选择目标 / 选项 / 生命牌触发等交互
 */
export default function PromptOverlay() {
  const serverPrompt = useGameStore((s) => s.pendingPrompt);
  const localOverflowHandIndex = useGameStore((s) => s.localOverflowHandIndex);
  const my = useGameStore((s) => s.my);
  const opp = useGameStore((s) => s.opponent);
  const spectatorNames = useGameStore((s) => s.spectatorNames);
  const flashPromptSuccess = useGameStore((s) => s.flashPromptSuccess);
  const clearLocalOverflow = useGameStore((s) => s.clearLocalOverflow);
  const [selected, setSelected] = useState<string[]>([]);
  const [submittingPromptId, setSubmittingPromptId] = useState<string | null>(null);
  const [isMinimized, setIsMinimized] = useState(false);
  const promptToggleOffset = spectatorNames.length > 0 ? "6.75rem" : "3.75rem";
  // 手机竖屏的对局画布会顺时针旋转 90°，这里必须使用布局层映射后的安全区变量，
  // 不能只依赖固定 bottom/left，否则 iOS Safari 的刘海或浏览器边缘会裁掉按钮。
  const promptToggleStyle = {
    left: `calc(${promptToggleOffset} + var(--layout-safe-left, 0px))`,
    bottom: "calc(0.75rem + var(--layout-safe-bottom, 0px))",
  } as const;
  // 竖屏设备中的对局实际运行在 844×390 的旋转容器内。效果确认框必须使用容器单位，
  // 否则 100vw/max-sm 会继续按物理竖屏宽度计算，把横向内容误排成遮满牌桌的纵向弹窗。
  const effectPromptStyle = {
    bottom: "calc(clamp(0.75rem, 4cqh, 1.5rem) + var(--layout-safe-bottom, 0px))",
    width:
      "min(42rem, calc(100cqw - 2rem - var(--layout-safe-left, 0px) - var(--layout-safe-right, 0px)))",
    maxHeight:
      "calc(100cqh - 2rem - var(--layout-safe-top, 0px) - var(--layout-safe-bottom, 0px))",
  } as const;
  const minimizedEffectPromptStyle = {
    bottom: "calc(0.75rem + var(--layout-safe-bottom, 0px))",
    maxWidth:
      "calc(100cqw - 2rem - var(--layout-safe-left, 0px) - var(--layout-safe-right, 0px))",
  } as const;

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
  const options = prompt?.extra?.options as string[] | undefined;
  const isEffectConfirm =
    prompt?.kind === "Option" &&
    options?.length === 2 &&
    options[0] === "是" &&
    options[1] === "否";

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
    if (isEffectConfirm) {
      return (
        <AnimatePresence>
          <motion.div
            className="pointer-events-none fixed inset-0 z-[60]"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
          >
            <motion.button
              type="button"
              onClick={() => setIsMinimized(false)}
              className="pointer-events-auto fixed left-1/2 flex -translate-x-1/2 items-center gap-3 border border-cyan-300/50 bg-slate-950/95 py-2 pl-4 pr-2 text-left text-xs font-bold text-slate-100 shadow-[0_0_28px_rgba(34,211,238,.2)] backdrop-blur-md"
              style={minimizedEffectPromptStyle}
              initial={{ y: 12, opacity: 0 }}
              animate={{ y: 0, opacity: 1 }}
              title="展开效果确认框"
              aria-label="展开效果确认框"
              aria-expanded="false"
            >
              <span className="min-w-0 truncate">等待确认：{prompt.text}</span>
              <span className="flex h-8 w-8 shrink-0 items-center justify-center border border-cyan-300/40 bg-cyan-400/10 text-cyan-100">
                <PromptChevron expanded={false} />
              </span>
            </motion.button>
          </motion.div>
        </AnimatePresence>
      );
    }

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
            style={promptToggleStyle}
            className="pointer-events-auto fixed flex h-12 w-12 items-center justify-center rounded-full bg-slate-800/90 text-[11px] font-bold text-white shadow-lg ring-1 ring-white/30 hover:bg-slate-700"
            title="恢复选择面板"
            aria-label="恢复选择面板"
          >
            恢复
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
  type DonChoice = {
    id: string;
    state: string;
    attachedToCardId?: string;
    attachedToNumber?: string;
    attachedToName?: string;
  };
  const donChoices = (prompt.extra?.donChoices as DonChoice[] | undefined) ?? [];
  const canCancelReturnDon = prompt.extra?.canCancel === true;
  // 通用选择分支里，凡 id 命中 donChoices 的渲染成咚 token（混合"卡牌 + 咚"同列，如 OP16-033 休置成本）
  const donChoiceMap = new Map(donChoices.map((d) => [d.id, d]));

  // 服务端注入的效果源卡号：让玩家知道当前在结算哪张卡的效果
  const sourceNumber = prompt.extra?.sourceNumber as string | undefined;
  const sourceCard = sourceNumber ? getGameCard(sourceNumber, my?.spriteMap) ?? null : null;
  const lifeCardNumber = prompt.extra?.lifeCardNumber as string | undefined;
  const lifeCard = lifeCardNumber ? getGameCard(lifeCardNumber, my?.spriteMap) ?? null : null;
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
    if (numFromExtra) return getGameCard(numFromExtra, my?.spriteMap) ?? null;
    // 领袖不在 fieldCards 里，需单独识别（候选 id 为领袖 GUID 或字面 "leader"），否则卡图无法加载
    if (id === "leader" || id === my?.leaderId)
      return my?.leaderNumber ? getGameCard(my.leaderNumber, my.spriteMap) ?? null : null;
    if (id === opp?.leaderId)
      return opp?.leaderNumber ? getGameCard(opp.leaderNumber, opp.spriteMap) ?? null : null;
    // 舞台不在 fieldCards 里（stageId/stageNumber 扁平字段），需单独识别，否则候选卡图加载不出
    if (my && id === my.stageId)
      return my.stageNumber ? getGameCard(my.stageNumber, my.spriteMap) ?? null : null;
    if (opp && id === opp.stageId)
      return opp.stageNumber ? getGameCard(opp.stageNumber, opp.spriteMap) ?? null : null;
    const myCard = my?.fieldCards.find((c) => c.id === id);
    if (myCard) return getGameCard(myCard.number, my?.spriteMap) ?? null;
    const opponentCard = opp?.fieldCards.find((c) => c.id === id);
    return opponentCard ? getGameCard(opponentCard.number, opp?.spriteMap) ?? null : null;
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

  if (isEffectConfirm) {
    return (
      <AnimatePresence>
        <motion.div
          className="pointer-events-none fixed inset-0 z-50"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          data-effect-confirm-layer
        >
          <motion.section
            className="pointer-events-auto fixed left-1/2 -translate-x-1/2 overflow-hidden border border-cyan-200/55 bg-[linear-gradient(180deg,rgba(8,15,27,.97),rgba(2,6,12,.94))] text-white shadow-[0_18px_60px_rgba(0,0,0,.72),0_0_36px_rgba(14,165,233,.16)] backdrop-blur-md"
            style={effectPromptStyle}
            initial={{ y: 36, opacity: 0, scale: 0.96 }}
            animate={{ y: 0, opacity: 1, scale: 1 }}
            exit={{ y: 24, opacity: 0, scale: 0.98 }}
            transition={{ type: "spring", stiffness: 360, damping: 30 }}
            role="dialog"
            aria-modal="false"
            aria-labelledby="effect-confirm-title"
            data-effect-confirm-dialog
          >
            <div className="pointer-events-none absolute inset-0 opacity-35 [background-image:repeating-linear-gradient(0deg,transparent,transparent_3px,rgba(125,211,252,.08)_4px)]" />
            <div className="relative flex flex-col @[640px]:flex-row">
              <div className="flex min-w-0 flex-1 items-center gap-3 px-4 py-3">
                <span className="h-2.5 w-2.5 shrink-0 rotate-45 bg-cyan-300 shadow-[0_0_10px_rgba(103,232,249,.9)]" />
                <div className="min-w-0 flex-1">
                  <div className="flex min-w-0 items-center gap-2">
                    <h2 id="effect-confirm-title" className="shrink-0 text-xs font-black tracking-[0.14em] text-cyan-50">
                      是否发动以下效果？
                    </h2>
                    {sourceCard && (
                      <span className="truncate text-[10px] font-bold text-amber-200" title={`${sourceCard.number} ${sourceCard.name}`}>
                        {sourceCard.number} · {sourceCard.name}
                      </span>
                    )}
                  </div>
                  <p className="mt-1 max-h-[3.75rem] overflow-y-auto pr-1 text-xs font-bold leading-5 text-slate-100">
                    {prompt.text}
                  </p>
                </div>
              </div>

              <div className="flex shrink-0 items-center justify-end gap-2 border-t border-white/10 bg-black/20 px-3 py-2 @[640px]:border-l @[640px]:border-t-0">
                <button
                  type="button"
                  onClick={() => submitServerPrompt(["1"])}
                  className="flex h-11 min-w-20 items-center justify-center gap-1.5 border border-rose-300/55 bg-rose-400/10 px-3 text-xs font-black text-rose-100 transition-colors hover:bg-rose-400/20 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-rose-200"
                  aria-label="取消"
                >
                  <span aria-hidden="true" className="text-lg leading-none">×</span>
                  取消
                </button>
                <button
                  type="button"
                  onClick={() => submitServerPrompt(["0"], true)}
                  className="flex h-11 min-w-20 items-center justify-center gap-1.5 border border-cyan-300/65 bg-cyan-400/15 px-3 text-xs font-black text-cyan-50 transition-colors hover:bg-cyan-400/25 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-cyan-100"
                  aria-label="确认"
                >
                  <span aria-hidden="true" className="text-base leading-none">✓</span>
                  确认
                </button>
                <button
                  type="button"
                  onClick={() => setIsMinimized(true)}
                  className="flex h-11 w-11 shrink-0 items-center justify-center border border-cyan-200/30 bg-cyan-300/10 text-cyan-100 transition-colors hover:bg-cyan-300/20 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-cyan-200"
                  title="收起效果确认框"
                  aria-label="收起效果确认框"
                  aria-expanded="true"
                >
                  <PromptChevron expanded />
                </button>
              </div>
            </div>
          </motion.section>
        </motion.div>
      </AnimatePresence>
    );
  }

  return (
    <AnimatePresence>
      <motion.div
        className="fixed inset-0 z-50 overflow-y-auto bg-black/75"
        initial={{ opacity: 0 }} animate={{ opacity: 1 }}
      >
        <button
          type="button"
          onClick={() => setIsMinimized(true)}
          style={promptToggleStyle}
          className="fixed z-[60] flex h-12 w-12 items-center justify-center rounded-full bg-slate-800/90 text-[11px] font-bold text-white shadow-lg ring-1 ring-white/30 hover:bg-slate-700"
          title="隐藏选择面板"
          aria-label="隐藏选择面板"
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
            <div className="flex max-w-3xl flex-wrap justify-center gap-3">
              {donChoices.map((d) => {
                const isSel = selected.includes(d.id);
                const isLeaderTarget = !!my && d.attachedToCardId === my.leaderId;
                const characterIndex = d.attachedToCardId
                  ? (my?.fieldCards.findIndex((card) => card.id === d.attachedToCardId) ?? -1)
                  : -1;
                const targetNumber = isLeaderTarget
                  ? my?.leaderNumber
                  : characterIndex >= 0
                    ? my?.fieldCards[characterIndex]?.number
                    : d.attachedToNumber;
                const attachedCard = targetNumber ? getGameCard(targetNumber, my?.spriteMap) ?? null : null;
                const targetPosition = isLeaderTarget
                  ? "领袖"
                  : characterIndex >= 0
                    ? `角色 ${characterIndex + 1}`
                    : "角色";
                const targetName = d.attachedToName ?? attachedCard?.name ?? "未知目标";
                const stateLabel = d.state === "Active" ? "活跃" : d.state === "Rest" ? "休息" : "附着";
                return (
                  <div
                    key={d.id}
                    onClick={() => toggle(d.id)}
                    title={d.state === "Attached" ? `贴在 ${targetPosition} · ${targetName}` : stateLabel}
                    className={`relative flex w-28 cursor-pointer flex-col items-center gap-1 rounded-lg border-2 p-2 transition ${
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
                    {d.state === "Attached" && (
                      <div className="mt-1 flex w-full flex-col items-center gap-1 border-t border-amber-200/20 pt-2">
                        {attachedCard && (
                          <CardItem
                            card={attachedCard}
                            size="sm"
                            hideCounter
                            liftOnSelect={false}
                          />
                        )}
                        <span className="rounded bg-amber-300 px-2 py-0.5 text-[10px] font-black text-black">
                          {targetPosition}
                        </span>
                        <span className="w-full break-words text-center text-[10px] leading-tight text-amber-100">
                          {targetName}
                        </span>
                      </div>
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
                const isLeaderChoice =
                  id === "leader" || id === my?.leaderId || id === opp?.leaderId || choiceZone === "leader";
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
                        {isLeaderChoice ? " · 领袖" : fieldIndex >= 0 ? ` · 第${fieldIndex + 1}位` : ""}
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
