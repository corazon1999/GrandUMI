"use client";

import { useGameStore } from "@/store/gameStore";
import { useBattleStore } from "@/store/battleStore";
import { useResponsive } from "@/hooks/useResponsive";
import CardItem from "@/components/ui/CardItem";
import { getCard } from "@/data/CardLoader";
import { GameRequest } from "@/net/GameRequest";

interface Props {
  side: "my" | "opponent";
}

export default function FieldArea({ side }: Props) {
  const player = useGameStore((s) => (side === "my" ? s.my : s.opponent));
  const isPending = useGameStore((s) => s.isPending);
  const selectedFieldId = useGameStore((s) => s.selectedFieldId);
  const selectedDonIndex = useGameStore((s) => s.selectedDonIndex);
  const setSelectedField = useGameStore((s) => s.setSelectedField);
  const setSelectedDon = useGameStore((s) => s.setSelectedDon);

  const battle = useGameStore((s) => s.battle);
  const currentTurn = useGameStore((s) => s.currentTurn);
  const turnCount = useGameStore((s) => s.turnCount);

  const isSelectingTarget = useBattleStore((s) => s.isSelectingTarget);
  const confirmAttackTarget = useBattleStore((s) => s.confirmAttackTarget);
  const { cardSize } = useResponsive();

  if (!player) return <div className="h-full min-h-0" />;

  // 战斗中：攻击方=当前回合方，防守方=另一方。按卡 id 区分攻击者 / 被攻击目标。
  const attackerSide = currentTurn ? "my" : "opponent";
  const defenderSide = currentTurn ? "opponent" : "my";

  const handleCardClick = (cardId: string, isTapped: boolean) => {
    if (isPending) return;

    if (isSelectingTarget && side === "opponent") {
      // 只有横置(休息)的角色才能被攻击；活跃角色不可成为目标
      if (!isTapped) return;
      confirmAttackTarget({ isLeader: false, cardId });
      return;
    }

    // 选中了活跃咚 + 点自己角色 → 贴咚
    if (selectedDonIndex !== null && side === "my") {
      GameRequest.attachDon(cardId);
      setSelectedDon(null);
      return;
    }

    setSelectedField(selectedFieldId === cardId ? null : cardId);
  };

  return (
    <div className="flex h-full min-h-0 min-w-0 items-center justify-center gap-3 overflow-visible rounded-md border border-sky-200/15 bg-black/15 px-4 py-3 shadow-inner shadow-black/30">
      {player.fieldCards.map((fc) => {
        const cardData = getCard(fc.number) ?? null;
        const attachedCount = fc.attachedDon;
        const isAttacker = !!battle && side === attackerSide && fc.id === battle.attackerCardId;
        const isBattleTarget =
          !!battle && side === defenderSide && !battle.targetIsLeader && fc.id === battle.targetCardId;
        // 选择攻击目标时：只有横置(休息)的对手角色可被选中
        const isAttackTarget = isSelectingTarget && side === "opponent" && !isPending && fc.isTapped;
        // 攻击状态标识：仅我方角色、我方回合显示。canAttack 来自后端权威；
        // 不可攻击且本回合刚登场(未横置)→ 召唤眩晕(sick)；其余不可攻击不额外标(横置已变灰)
        const attackState: "can" | "sick" | "none" =
          side === "my" && currentTurn
            ? fc.canAttack
              ? "can"
              : !fc.isTapped && fc.turnPlayed === turnCount
                ? "sick"
                : "none"
            : "none";

        return (
          <div key={fc.id} className="relative flex h-full min-h-0 shrink-0 items-center">
            <div className="relative">
              {/* 战斗高亮：攻击者红环 / 被攻击目标琥珀环 */}
              {isAttacker && (
                <div className="pointer-events-none absolute -inset-1 z-20 rounded-lg ring-4 ring-red-500 animate-pulse shadow-lg shadow-red-500/50" />
              )}
              {isBattleTarget && (
                <div className="pointer-events-none absolute -inset-1 z-20 rounded-lg ring-4 ring-amber-400 animate-pulse shadow-lg shadow-amber-400/50" />
              )}
              {isAttacker && (
                <span className="pointer-events-none absolute -top-3 left-1/2 z-30 -translate-x-1/2 rounded bg-red-600 px-1.5 text-[10px] font-black text-white shadow">
                  攻击
                </span>
              )}
              {isBattleTarget && (
                <span className="pointer-events-none absolute -top-3 left-1/2 z-30 -translate-x-1/2 rounded bg-amber-500 px-1.5 text-[10px] font-black text-black shadow">
                  目标
                </span>
              )}
              <CardItem
                card={cardData}
                isSelected={selectedFieldId === fc.id || isAttackTarget}
                isTapped={fc.isTapped}
                powerBuff={fc.powerCurrent - (cardData?.power ?? 0) - attachedCount * 1000}
                costBuff={fc.cost - (cardData?.cost ?? 0)}
                attachedDonCount={attachedCount}
                hideCounter
                liftOnSelect={false}
                showBlockerFx
                attackState={attackState}
                size={cardSize}
                onClick={() => handleCardClick(fc.id, fc.isTapped)}
              />
              {isAttackTarget && (
                <div className="absolute -right-2 -top-2 h-5 w-5 animate-pulse rounded-full bg-red-500 shadow-lg shadow-red-500/50" />
              )}
              {selectedDonIndex !== null && side === "my" && !isPending && (
                <div className="absolute -left-2 -top-2 z-40 flex h-6 w-6 animate-pulse items-center justify-center rounded-full bg-yellow-300 shadow-lg shadow-yellow-300/50">
                  <span className="text-[10px] font-black text-black">+</span>
                </div>
              )}
            </div>
          </div>
        );
      })}

      {player.fieldCards.length === 0 && (
        <span className="text-xs font-semibold text-slate-600">
          {side === "my" ? "角色区" : "对手角色区"}
        </span>
      )}
    </div>
  );
}
