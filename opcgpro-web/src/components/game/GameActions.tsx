"use client";

import { useEffect, useState, useSyncExternalStore } from "react";
import { useGameStore } from "@/store/gameStore";
import { useBattleStore } from "@/store/battleStore";
import { useIsDefender } from "@/hooks/useIsDefender";
import {
  GameRequest,
  getPendingAttachDonUndo,
  subscribePendingAttachDonUndo,
  undoLastPendingAttachDon,
} from "@/net/GameRequest";
import { getCard } from "@/data/CardLoader";
import { canPayActivatedMainCost } from "@/lib/activatedMainCost";
import { useLayoutQuarterTurn } from "@/components/ui/ResponsiveScope";

/**
 * 上下文操作按钮区：根据当前阶段/选中状态展示可用动作。
 * 放在右侧栏"操作"小区，与"操作日志"区分隔。
 */
export default function GameActions() {
  const [isEndTurnConfirming, setIsEndTurnConfirming] = useState(false);
  const rotateQuarterTurn = useLayoutQuarterTurn();
  const pendingAttachDonUndo = useSyncExternalStore(
    subscribePendingAttachDonUndo,
    getPendingAttachDonUndo,
    () => null,
  );
  const currentTurn = useGameStore((s) => s.currentTurn);
  const phase = useGameStore((s) => s.phase);
  const isPending = useGameStore((s) => s.isPending);
  const selectedHandIndex = useGameStore((s) => s.selectedHandIndex);
  const selectedFieldId = useGameStore((s) => s.selectedFieldId);
  const turnCount = useGameStore((s) => s.turnCount);
  const battle = useGameStore((s) => s.battle);
  const my = useGameStore((s) => s.my);
  const setSelectedField = useGameStore((s) => s.setSelectedField);
  const openLocalOverflow = useGameStore((s) => s.openLocalOverflow);
  const isDefender = useIsDefender();

  const endTurn = useBattleStore((s) => s.endTurn);
  const startAttack = useBattleStore((s) => s.startAttack);
  const cancelAttack = useBattleStore((s) => s.cancelAttack);
  const isSelectingTarget = useBattleStore((s) => s.isSelectingTarget);

  // 攻击机会以后端权威字段为准，避免仅凭“活跃状态”展示实际不可发动的攻击按钮。
  const selectedAttackerCanAttack =
    my && selectedFieldId !== null
      ? my.leaderId === selectedFieldId
        ? my.leaderCanAttack
        : (my.fieldCards.find((c) => c.id === selectedFieldId)?.canAttack ?? false)
      : false;
  const canAttack =
    currentTurn && turnCount > 1 && !battle && !isSelectingTarget && selectedAttackerCanAttack;
  const canPlay = currentTurn && selectedHandIndex !== null;
  const canPassCounter = isDefender && phase === "Counter";
  const selectedFieldCard =
    my && selectedFieldId !== null
      ? my.fieldCards.find((card) => card.id === selectedFieldId)
      : undefined;

  // 选中场上卡（领袖/领航、角色或舞台）含【启动主要】时，主要阶段可发动启动效果
  const selectedNumber =
    my && selectedFieldId !== null
      ? selectedFieldId === my.leaderId
          ? my.leaderNumber
          : selectedFieldId === my.stageId
            ? my.stageNumber
          : (selectedFieldCard?.number ?? null)
      : null;
  const selectedHasActivated = selectedNumber
    ? (getCard(selectedNumber)?.effectTags?.includes("ActivatedMain") ?? false)
    : false;
  // 该启动效果若为【每回合1次】且本回合已发动过，则后端权威下发已用标记 → 隐藏按钮
  const selectedActivatedUsed =
    my && selectedFieldId !== null
      ? selectedFieldId === my.leaderId
        ? my.leaderActivatedUsedThisTurn
        : selectedFieldId === my.stageId
          ? my.stageActivatedUsedThisTurn
          : (selectedFieldCard?.activatedUsedThisTurn ?? false)
      : false;
  const canActivate =
    currentTurn &&
    phase === "Main" &&
    !battle &&
    !isSelectingTarget &&
    selectedFieldId !== null &&
    selectedHasActivated &&
    !selectedActivatedUsed &&
    canPayActivatedMainCost(
      selectedNumber,
      selectedFieldCard?.isTapped ?? false,
      selectedFieldCard?.cannotBeRested ?? false,
    );

  // 贴咚采用“目标优先”操作：先选中领袖/角色，再直接选择要赋予的张数。
  // 领袖在协议中使用固定标识 "leader"；角色沿用场上实例 ID，舞台不可贴咚。
  const attachTargetId =
    my && selectedFieldId !== null
      ? selectedFieldId === my.leaderId
        ? "leader"
        : my.fieldCards.some((card) => card.id === selectedFieldId)
          ? selectedFieldId
          : null
      : null;
  const canAttachDon =
    currentTurn &&
    phase === "Main" &&
    !battle &&
    !isSelectingTarget &&
    attachTargetId !== null &&
    (my?.costActive ?? 0) > 0;
  const attachDonCounts = Array.from({ length: my?.costActive ?? 0 }, (_, index) => index + 1);
  useEffect(() => {
    if (!isEndTurnConfirming) return;
    const timer = window.setTimeout(() => setIsEndTurnConfirming(false), 3_000);
    return () => window.clearTimeout(timer);
  }, [isEndTurnConfirming]);

  useEffect(() => {
    setIsEndTurnConfirming(false);
  }, [currentTurn, phase, turnCount, selectedHandIndex, selectedFieldId, isSelectingTarget]);

  const btn =
    "min-h-12 w-full rounded-md px-3 py-2 text-sm font-bold text-white shadow transition-colors disabled:cursor-not-allowed disabled:bg-gray-600";

  const hasAny =
    pendingAttachDonUndo !== null ||
    canAttack || isSelectingTarget || canPlay || canActivate || canPassCounter || currentTurn;

  const activateEffect = () => {
    if (!selectedFieldId) return;
    GameRequest.useEffect(selectedFieldId, "main");
    setSelectedField(null);
  };

  const playSelectedCard = () => {
    if (selectedHandIndex === null || !my) return;
    const selected = getCard(my.handCardNumbers[selectedHandIndex] ?? "");
    if (selected?.type === "Character" && my.fieldCards.length >= 5) {
      openLocalOverflow(selectedHandIndex);
      return;
    }
    GameRequest.playCard(selectedHandIndex);
  };

  const attachDon = (count: number) => {
    if (!attachTargetId) return;
    GameRequest.attachDon(attachTargetId, count);
    setSelectedField(null);
  };

  const requestEndTurn = () => {
    setIsEndTurnConfirming(true);
  };

  const confirmEndTurn = () => {
    setIsEndTurnConfirming(false);
    endTurn();
  };

  return (
    <div className="flex flex-col gap-2">
      {pendingAttachDonUndo && (
        <div
          className="rounded-md border border-amber-300/40 bg-amber-950/45 p-2 shadow-inner shadow-black/20"
          role="status"
        >
          <p className="mb-2 text-center text-[11px] font-bold leading-4 text-amber-50">
            已贴 {pendingAttachDonUndo.count} 咚
            {pendingAttachDonUndo.queuedCount > 1 ? `（共 ${pendingAttachDonUndo.queuedCount} 次待提交）` : ""}
            ；执行下一项操作后将无法撤回
          </p>
          <button
            type="button"
            onClick={undoLastPendingAttachDon}
            disabled={isPending}
            className={`${rotateQuarterTurn ? "min-h-[5.75rem]" : "min-h-12"} w-full rounded-md border border-amber-200/60 bg-amber-400 px-3 py-2 text-sm font-black text-slate-950 shadow transition-colors hover:bg-amber-300 disabled:cursor-not-allowed disabled:bg-gray-600 disabled:text-gray-300`}
          >
            撤回贴咚
          </button>
        </div>
      )}

      {canAttack && (
        <button
          onClick={() => selectedFieldId && startAttack(selectedFieldId)}
          disabled={isPending}
          className={`${btn} bg-red-600 hover:bg-red-500`}
        >
          攻击
        </button>
      )}

      {isSelectingTarget && (
        <button
          onClick={cancelAttack}
          disabled={isPending}
          className={`${btn} bg-slate-700 hover:bg-slate-600`}
        >
          取消攻击
        </button>
      )}

      {canPlay && (
        <button
          onClick={playSelectedCard}
          disabled={isPending}
          className={`${btn} bg-blue-500 hover:bg-blue-400`}
        >
          出牌
        </button>
      )}

      {canActivate && (
        <button
          onClick={activateEffect}
          disabled={isPending}
          className={`${btn} bg-purple-600 hover:bg-purple-500`}
        >
          启动效果
        </button>
      )}

      {canAttachDon && (
        <div className="rounded-md border border-amber-300/25 bg-amber-950/25 p-2 shadow-inner shadow-black/20">
          <div className="mb-1.5 flex items-center justify-between gap-2 text-[11px] font-black">
            <span className="text-amber-100">贴咚</span>
            <span className="text-amber-300/80">可用 {my?.costActive ?? 0}</span>
          </div>
          <div className="grid grid-cols-5 gap-1">
            {attachDonCounts.map((count) => (
              <button
                key={count}
                type="button"
                onClick={() => attachDon(count)}
                disabled={isPending}
                title={`赋予 ${count} 张咚!!`}
                className="min-h-11 rounded bg-amber-500/85 text-xs font-black text-black shadow transition-colors hover:bg-amber-300 disabled:cursor-not-allowed disabled:bg-gray-600 disabled:text-gray-300"
              >
                {count}
              </button>
            ))}
          </div>
          {attachDonCounts.length > 1 && (
            <button
              type="button"
              onClick={() => attachDon(attachDonCounts.length)}
              disabled={isPending}
              className="mt-1.5 min-h-11 w-full rounded bg-amber-700/80 text-[11px] font-black text-amber-50 shadow transition-colors hover:bg-amber-600 disabled:cursor-not-allowed disabled:bg-gray-600"
            >
              全部（{attachDonCounts.length}）
            </button>
          )}
        </div>
      )}

      {canPassCounter && (
        <button
          onClick={() => GameRequest.passCounter()}
          disabled={isPending}
          className={`${btn} bg-amber-600 hover:bg-amber-500`}
        >
          结束反击
        </button>
      )}

      {currentTurn && (
        <div className="mt-2 border-t border-rose-200/20 pt-4">
          <p className="mb-2 text-[10px] font-black tracking-[0.18em] text-slate-500">
            回合控制
          </p>
          {isEndTurnConfirming ? (
            <div
              className="rounded-md border border-rose-300/30 bg-rose-950/35 p-2"
              role="group"
              aria-label="确认结束回合"
            >
              <p className="mb-2 text-center text-[11px] font-bold text-rose-100">
                确定结束回合？
              </p>
              <div className="grid grid-cols-2 gap-2">
                <button
                  type="button"
                  onClick={() => setIsEndTurnConfirming(false)}
                  disabled={isPending}
                  className="min-h-12 rounded-md bg-slate-700 px-2 py-2 text-xs font-bold text-white transition-colors hover:bg-slate-600 disabled:cursor-not-allowed disabled:bg-gray-600"
                >
                  取消
                </button>
                <button
                  type="button"
                  onClick={confirmEndTurn}
                  disabled={isPending}
                  className="min-h-12 rounded-md bg-rose-600 px-2 py-2 text-xs font-bold text-white shadow transition-colors hover:bg-rose-500 disabled:cursor-not-allowed disabled:bg-gray-600"
                >
                  确认结束
                </button>
              </div>
            </div>
          ) : (
            <button
              type="button"
              onClick={requestEndTurn}
              disabled={isPending}
              className={`${btn} border border-rose-300/35 bg-rose-950/65 text-rose-50 hover:bg-rose-900/80`}
            >
              结束回合
            </button>
          )}
        </div>
      )}

      {!hasAny && (
        <p className="py-1 text-center text-xs text-slate-500">等待对手…</p>
      )}
    </div>
  );
}
