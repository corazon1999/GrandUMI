"use client";

import { useGameStore } from "@/store/gameStore";
import { useResponsive } from "@/hooks/useResponsive";
import DonCardItem from "./DonCardItem";

interface Props {
  side: "my" | "opponent";
}

const slotSizes = {
  sm: "h-[6.3rem] w-[4.5rem]",
  md: "h-[8.4rem] w-[6rem]",
  lg: "h-[11.2rem] w-[8rem]",
};

const restSlotSizes = {
  sm: "h-[4.5rem] w-[6.3rem]",
  md: "h-[6rem] w-[8.4rem]",
  lg: "h-[8rem] w-[11.2rem]",
};

function DonCountSlot({
  label,
  count,
  state,
  selected,
  stagedCount,
  canInteract,
  onClick,
}: {
  label: string;
  count: number;
  state: "active" | "rest";
  selected?: boolean;
  stagedCount?: number;
  canInteract?: boolean;
  onClick?: () => void;
}) {
  const { cardSize } = useResponsive();

  return (
    <button
      type="button"
      onClick={count > 0 && canInteract ? onClick : undefined}
      disabled={count <= 0 || !canInteract}
      className={`${state === "rest" ? restSlotSizes[cardSize] : slotSizes[cardSize]} relative shrink-0 rounded-md border border-sky-200/15 bg-black/15 text-left shadow-inner shadow-black/25 disabled:cursor-default`}
    >
      <span className={`absolute left-2 top-2 z-20 text-[11px] font-black drop-shadow ${state === "active" ? "text-yellow-200" : "text-zinc-200"}`}>
        {label}
      </span>
      {count > 0 ? (
        <DonCardItem state={state} size={cardSize} isSelected={selected} disabled />
      ) : (
        <div className="h-full w-full rounded-md border-2 border-dashed border-slate-500/50 bg-slate-950/35" />
      )}
      {/* 拟依附张数徽标：再点 +1，达上限后再点取消（#144 复数咚依附） */}
      {stagedCount && stagedCount > 0 ? (
        <span className="absolute right-1 top-1 z-30 rounded-full bg-amber-400 px-1.5 py-0.5 text-[10px] font-black leading-none text-black shadow ring-2 ring-amber-200">
          依附×{stagedCount}
        </span>
      ) : null}
      <div className="absolute inset-x-0 bottom-1 z-30 flex justify-center">
        <span className="rounded bg-slate-950/90 px-2 py-0.5 text-xs font-black text-white shadow">
          {count}
        </span>
      </div>
    </button>
  );
}

export default function DonArea({ side }: Props) {
  const player = useGameStore((s) => (side === "my" ? s.my : s.opponent));
  const currentTurn = useGameStore((s) => s.currentTurn);
  const isPending = useGameStore((s) => s.isPending);
  const selectedDonIndex = useGameStore((s) => s.selectedDonIndex);
  const setSelectedDon = useGameStore((s) => s.setSelectedDon);

  if (!player) return null;

  const canInteract = side === "my" && currentTurn && !isPending && player.costActive > 0;

  return (
    <div className="flex min-w-0 items-center gap-2">
      <DonCountSlot
        label="活跃"
        count={player.costActive}
        state="active"
        selected={selectedDonIndex !== null}
        stagedCount={selectedDonIndex ?? 0}
        canInteract={canInteract}
        // 每次点击拟依附数 +1（封顶=活跃咚数），到顶后再点取消；目标点击时一次依附该数量
        onClick={() => {
          const cur = selectedDonIndex ?? 0;
          setSelectedDon(cur >= player.costActive ? null : cur + 1);
        }}
      />
      <DonCountSlot
        label="休息"
        count={player.costRest}
        state="rest"
      />
    </div>
  );
}
