"use client";

import type { StandardRankedCardPool } from "@/types/net";
import { STANDARD_RANKED_CARD_POOLS } from "@/lib/rankedCardPool";

export default function RankedCardPoolPanel({ value, onChange, disabled, ready }: {
  value: StandardRankedCardPool;
  onChange: (pool: StandardRankedCardPool) => void;
  disabled: boolean;
  ready: boolean;
}) {
  const selected = STANDARD_RANKED_CARD_POOLS.find(pool => pool.value === value)!;
  return <fieldset aria-label="标准排位卡池选择" disabled={disabled || !ready}
    data-ranked-card-pool={value} data-pool-ready={ready ? "true" : "false"}
    className="min-w-0 rounded-xl border border-violet-900/70 bg-gray-950 p-3">
    <legend className="px-1 text-sm font-bold text-violet-200">选择卡池</legend>
    <div className="grid grid-cols-1 gap-2 @[480px]:grid-cols-3">
      {STANDARD_RANKED_CARD_POOLS.map(pool => <button key={pool.value} type="button"
        data-card-pool-choice={pool.value} aria-pressed={value === pool.value}
        onClick={() => onChange(pool.value)}
        className={`min-h-16 min-w-0 rounded-lg border px-3 py-2 text-left transition-colors disabled:opacity-50 ${value === pool.value
          ? "border-violet-400 bg-violet-950/60 text-white" : "border-gray-800 text-gray-400 hover:border-violet-700 hover:bg-gray-900"}`}>
        <span className="block break-words text-sm font-bold">{pool.label}</span>
        <span className="mt-0.5 block text-xs text-gray-400">{pool.subtitle}</span>
      </button>)}
    </div>
    <p className="mt-2 text-xs leading-5 text-gray-400">{selected.description}</p>
    <p className="mt-1 text-[11px] leading-4 text-gray-500">自动记住此账号在当前浏览器的上次选择。</p>
  </fieldset>;
}
