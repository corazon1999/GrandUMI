"use client";

import { useGameStore } from "@/store/gameStore";
import { useBattleStore } from "@/store/battleStore";
import { useResponsive } from "@/hooks/useResponsive";
import CardItem from "@/components/ui/CardItem";
import { getGameCard } from "@/data/CardLoader";
import { GameRequest } from "@/net/GameRequest";
import BattleTargetBadge from "@/components/game/BattleTargetBadge";

interface Props {
  side: "my" | "opponent";
}

export default function FieldArea({ side }: Props) {
  const player = useGameStore((s) => (side === "my" ? s.my : s.opponent));
  const my = useGameStore((s) => s.my);
  const isPending = useGameStore((s) => s.isPending);
  const selectedFieldId = useGameStore((s) => s.selectedFieldId);
  const selectedDonIndex = useGameStore((s) => s.selectedDonIndex);
  const setSelectedField = useGameStore((s) => s.setSelectedField);
  const setSelectedDon = useGameStore((s) => s.setSelectedDon);

  const battle = useGameStore((s) => s.battle);
  const phase = useGameStore((s) => s.phase);
  const currentTurn = useGameStore((s) => s.currentTurn);
  const turnCount = useGameStore((s) => s.turnCount);

  const isSelectingTarget = useBattleStore((s) => s.isSelectingTarget);
  const attackerId = useBattleStore((s) => s.attackerId);
  const confirmAttackTarget = useBattleStore((s) => s.confirmAttackTarget);
  const { cardSize } = useResponsive();

  if (!player) return <div className="h-full min-h-0" />;

  const attackerCanAttackActive = !!my && !!attackerId && (
    (my.leaderId === attackerId && my.leaderGainedKeywords.includes("可攻击活跃"))
    || (my.fieldCards.find((card) => card.id === attackerId)?.gainedKeywords.includes("可攻击活跃") ?? false)
  );

  const handleCardClick = (cardId: string, isTapped: boolean) => {
    if (isPending) return;

    if (isSelectingTarget && side === "opponent") {
      // 通常只能攻击休息角色；获得“可攻击活跃”的攻击者也可点选活跃角色。
      if (!isTapped && !attackerCanAttackActive) return;
      confirmAttackTarget({ isLeader: false, cardId });
      return;
    }

    // 选中了活跃咚 + 点自己角色 → 贴咚（依附拟选的张数，#144）
    if (selectedDonIndex !== null && side === "my") {
      GameRequest.attachDon(cardId, selectedDonIndex || 1);
      setSelectedDon(null);
      return;
    }

    setSelectedField(selectedFieldId === cardId ? null : cardId);
  };

  return (
    <div
      className="flex h-full min-h-0 min-w-0 items-center justify-center gap-8 overflow-visible rounded-md border border-sky-200/15 bg-black/15 px-4 py-3 shadow-inner shadow-black/30"
      data-zone="field"
      data-zone-side={side}
    >
      {player.fieldCards.map((fc) => {
        const cardData = getGameCard(fc.number, player.spriteMap) ?? null;
        const attachedCount = fc.attachedDon;
        const isAttacker = !!battle && fc.id === battle.attackerCardId;
        const isBlocker = !!battle && fc.id === battle.blockerCardId;
        const isBattleTarget =
          isBlocker || (!!battle && !battle.blockerCardId && !battle.targetIsLeader && fc.id === battle.targetCardId);
        const isAttackTarget = isSelectingTarget
          && side === "opponent"
          && !isPending
          && (fc.isTapped || attackerCanAttackActive);
        // 明确的禁攻状态对敌我双方、任意回合都可见；其他攻击状态仅在我方回合显示。
        // 新登场且未横置时显示召唤眩晕(sick)，横置等普通不可攻击条件不额外标记。
        const attackState: "can" | "sick" | "blocked" | "none" =
          fc.cannotAttack
            ? "blocked"
            : side === "my" && currentTurn
            ? fc.canAttack
              ? "can"
              : !fc.isTapped && fc.turnPlayed === turnCount
                ? "sick"
                : "none"
            : "none";

        return (
          <div key={fc.id} className="relative flex h-full min-h-0 shrink-0 items-center">
            <div
              data-battle-card-id={fc.id}
              data-zone="field"
              data-zone-side={side}
              data-zone-card-id={fc.id}
              className={[
                "relative",
                isAttacker ? (side === "my" ? "battle-attacker-lunge-up" : "battle-attacker-lunge-down") : "",
                isBattleTarget ? "battle-target-impact" : "",
              ].join(" ")}
            >
              {isAttacker && (
                <span className="pointer-events-none absolute -top-3 left-1/2 z-30 -translate-x-1/2 rounded bg-red-600 px-1.5 text-[10px] font-black text-white shadow">
                  攻击
                </span>
              )}
              <BattleTargetBadge
                phase={phase}
                isBattleTarget={isBattleTarget}
                isBlocker={isBlocker}
              />
              {fc.effectsNullified && (
                <span
                  title="本回合角色效果无效"
                  className="pointer-events-none absolute -left-2 -top-2 z-40 rounded-full bg-slate-950/95 px-2 py-1 text-[10px] font-black text-fuchsia-200 shadow-lg ring-2 ring-fuchsia-400/80"
                >
                  效果无效
                </span>
              )}
              <CardItem
                card={cardData}
                isSelected={selectedFieldId === fc.id || isAttackTarget}
                isTapped={fc.isTapped}
                battleHighlight={isAttacker ? "attacker" : isBlocker ? "blocker" : isBattleTarget ? "target" : undefined}
                powerBuff={fc.powerCurrent - (cardData?.power ?? 0) - attachedCount * 1000}
                costBuff={fc.cost - (cardData?.cost ?? 0)}
                attachedDonCount={attachedCount}
                hideCounter
                liftOnSelect={false}
                showKeywordFx
                gainedKeywords={fc.gainedKeywords}
                attackState={attackState}
                oncePerTurnEffectAvailable={fc.oncePerTurnEffectAvailable}
                size={cardSize}
                onClick={() => handleCardClick(fc.id, fc.isTapped)}
              />
              {isAttackTarget && (
                <div className="absolute -right-2 -top-2 h-5 w-5 animate-pulse rounded-full bg-red-500 shadow-lg shadow-red-500/50" />
              )}
              {/* 锁定标识：被「下个重置阶段不会转为活跃」效果命中的角色（如 OP16-030） */}
              {fc.cannotActivateNextReset && (
                <div
                  title="下个重置阶段不会转为活跃"
                  className="pointer-events-none absolute -bottom-2 -right-2 z-40 flex h-6 w-6 items-center justify-center rounded-full bg-slate-900/90 text-amber-300 shadow-lg ring-2 ring-amber-400/70"
                >
                  <span className="text-[12px] leading-none">🔒</span>
                </div>
              )}
              {/* 无法转为休息状态：横置矩形(休息态卡牌)+红×(不能)，如 OP11-034/OP14-033/EB02-011 */}
              {fc.cannotBeRested && (
                <div
                  title="无法被效果转为休息状态"
                  className="pointer-events-none absolute -bottom-2 -left-2 z-40 flex h-6 w-6 items-center justify-center rounded-full bg-slate-900/90 shadow-lg ring-2 ring-rose-400/70"
                >
                  <svg viewBox="0 0 24 24" className="h-4 w-4" fill="none">
                    {/* 横置的卡牌（休息状态）：横向圆角矩形 */}
                    <rect x="3" y="8.5" width="18" height="7" rx="1.5" stroke="#e2e8f0" strokeWidth="1.6" />
                    {/* 覆盖的 × 表示"不能转为该状态" */}
                    <path d="M5.5 6 L18.5 18 M18.5 6 L5.5 18" stroke="#f43f5e" strokeWidth="2.2" strokeLinecap="round" />
                  </svg>
                </div>
              )}
              {selectedDonIndex !== null && side === "my" && !isPending && (
                <div className="absolute -left-2 -top-2 z-40 flex h-6 min-w-6 animate-pulse items-center justify-center rounded-full bg-yellow-300 px-1 shadow-lg shadow-yellow-300/50">
                  <span className="text-[10px] font-black text-black">+{selectedDonIndex}</span>
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
