"use client";

import { useGameStore } from "@/store/gameStore";
import { useBattleStore } from "@/store/battleStore";
import { useIsDefender } from "@/hooks/useIsDefender";
import { GameRequest } from "@/net/GameRequest";
import { getCard } from "@/data/CardLoader";

/**
 * 上下文操作按钮区：根据当前阶段/选中状态展示可用动作。
 * 放在右侧栏"操作"小区，与"操作日志"区分隔。
 */
export default function GameActions() {
  const currentTurn = useGameStore((s) => s.currentTurn);
  const phase = useGameStore((s) => s.phase);
  const isPending = useGameStore((s) => s.isPending);
  const selectedHandIndex = useGameStore((s) => s.selectedHandIndex);
  const selectedFieldId = useGameStore((s) => s.selectedFieldId);
  const turnCount = useGameStore((s) => s.turnCount);
  const battle = useGameStore((s) => s.battle);
  const my = useGameStore((s) => s.my);
  const setSelectedField = useGameStore((s) => s.setSelectedField);
  const isDefender = useIsDefender();

  const endTurn = useBattleStore((s) => s.endTurn);
  const startAttack = useBattleStore((s) => s.startAttack);
  const cancelAttack = useBattleStore((s) => s.cancelAttack);
  const isSelectingTarget = useBattleStore((s) => s.isSelectingTarget);

  // 选中的攻击者（领袖或己方角色）的横置状态；非攻击者返回 null
  const attackerTapped =
    my && selectedFieldId !== null
      ? my.leaderId === selectedFieldId
        ? my.leaderTapped
        : (my.fieldCards.find((c) => c.id === selectedFieldId)?.isTapped ?? null)
      : null;
  // 攻击者未横置才可宣言攻击
  const canAttack =
    currentTurn && turnCount > 1 && !battle && !isSelectingTarget && attackerTapped === false;
  const canPlay = currentTurn && selectedHandIndex !== null;
  const canPassCounter = isDefender && phase === "Counter";

  // 选中场上卡（领袖/领航、角色或舞台）含【启动主要】时，主要阶段可发动启动效果
  const selectedNumber =
    my && selectedFieldId !== null
      ? selectedFieldId === my.leaderId
        ? my.leaderNumber
        : selectedFieldId === my.stageId
          ? my.stageNumber
          : (my.fieldCards.find((c) => c.id === selectedFieldId)?.number ?? null)
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
          : (my.fieldCards.find((c) => c.id === selectedFieldId)?.activatedUsedThisTurn ?? false)
      : false;
  const canActivate =
    currentTurn &&
    phase === "Main" &&
    !battle &&
    !isSelectingTarget &&
    selectedFieldId !== null &&
    selectedHasActivated &&
    !selectedActivatedUsed;

  const btn =
    "w-full rounded-md px-3 py-2 text-sm font-bold text-white shadow transition-colors disabled:cursor-not-allowed disabled:bg-gray-600";

  const hasAny =
    canAttack || isSelectingTarget || canPlay || canActivate || canPassCounter || currentTurn;

  const activateEffect = () => {
    if (!selectedFieldId) return;
    GameRequest.useEffect(selectedFieldId, "main");
    setSelectedField(null);
  };

  return (
    <div className="flex flex-col gap-2">
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
          onClick={() => GameRequest.playCard(selectedHandIndex!)}
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
        <button
          onClick={endTurn}
          disabled={isPending}
          className={`${btn} bg-orange-500 hover:bg-orange-400`}
        >
          结束回合
        </button>
      )}

      {!hasAny && (
        <p className="py-1 text-center text-xs text-slate-500">等待对手…</p>
      )}
    </div>
  );
}
