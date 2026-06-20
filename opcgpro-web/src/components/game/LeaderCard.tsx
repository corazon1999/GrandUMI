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

const slotSizes = {
  sm: "w-[4.5rem] h-[6.3rem]",
  md: "w-[6rem] h-[8.4rem]",
  lg: "w-[8rem] h-[11.2rem]",
};

function EmptyLeader({ label }: { label: string }) {
  return (
    <div className={`${label} relative flex items-center justify-center rounded-md border border-dashed border-sky-200/25 bg-black/20 shadow-inner shadow-black/30`}>
      <span className="text-xs font-black text-slate-600">LEADER</span>
    </div>
  );
}

export default function LeaderCard({ side }: Props) {
  const player = useGameStore((s) => (side === "my" ? s.my : s.opponent));
  const isPending = useGameStore((s) => s.isPending);
  const selectedFieldId = useGameStore((s) => s.selectedFieldId);
  const setSelectedField = useGameStore((s) => s.setSelectedField);
  const selectedDonIndex = useGameStore((s) => s.selectedDonIndex);
  const setSelectedDon = useGameStore((s) => s.setSelectedDon);

  const isSelectingTarget = useBattleStore((s) => s.isSelectingTarget);
  const confirmAttackTarget = useBattleStore((s) => s.confirmAttackTarget);
  const battle = useGameStore((s) => s.battle);
  const currentTurn = useGameStore((s) => s.currentTurn);
  const { cardSize } = useResponsive();
  const dimensions = slotSizes[cardSize];

  if (!player) {
    return <EmptyLeader label={dimensions} />;
  }

  // 战斗高亮：攻击方=当前回合方；领袖可为攻击者或被攻击目标
  const attackerSide = currentTurn ? "my" : "opponent";
  const defenderSide = currentTurn ? "opponent" : "my";
  const isAttacker = !!battle && side === attackerSide && player.leaderId === battle.attackerCardId;
  const isBattleTarget = !!battle && side === defenderSide && battle.targetIsLeader;

  const isTargetable = isSelectingTarget && side === "opponent" && !isPending;

  const handleClick = () => {
    if (isPending) return;
    if (isTargetable) {
      confirmAttackTarget({ isLeader: true });
      return;
    }
    // 选中了活跃咚 + 点自己领袖 → 贴咚
    if (selectedDonIndex !== null && side === "my") {
      GameRequest.attachDon("leader");
      setSelectedDon(null);
      return;
    }
    if (side === "my") {
      setSelectedField(selectedFieldId === player.leaderId ? null : player.leaderId);
    }
  };

  const leader = getCard(player.leaderNumber);
  if (!leader) {
    return (
      <div className={`${dimensions} relative flex items-center justify-center rounded-md border border-dashed border-sky-200/25 bg-black/20 shadow-inner shadow-black/30`}>
        <span className="text-[10px] text-gray-500">{player.leaderNumber}</span>
      </div>
    );
  }

  return (
    <div className={`${dimensions} relative`}>
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
        card={leader}
        size={cardSize}
        isSelected={(side === "my" && selectedFieldId === player.leaderId) || isTargetable}
        isTapped={player.leaderTapped}
        attachedDonCount={player.leaderAttachedDon}
        powerBuff={player.leaderPower - (leader.power ?? 0) - player.leaderAttachedDon * 1000}
        hideCost
        attackState={side === "my" && currentTurn && player.leaderCanAttack ? "can" : "none"}
        onClick={handleClick}
      />
      {isTargetable && (
        <div className="pointer-events-none absolute -right-2 -top-2 h-5 w-5 animate-pulse rounded-full bg-red-500 shadow-lg shadow-red-500/50" />
      )}
      {selectedDonIndex !== null && side === "my" && !isPending && (
        <div className="pointer-events-none absolute -left-2 -top-2 flex h-6 w-6 animate-pulse items-center justify-center rounded-full bg-yellow-300 shadow-lg shadow-yellow-300/50">
          <span className="text-[10px] font-black text-black">+</span>
        </div>
      )}
    </div>
  );
}
