"use client";

import { motion, AnimatePresence } from "framer-motion";
import { useGameStore } from "@/store/gameStore";
import { useIsDefender } from "@/hooks/useIsDefender";
import { GameRequest } from "@/net/GameRequest";
import { getCard } from "@/data/CardLoader";
import CardItem from "@/components/ui/CardItem";

/**
 * 阻挡步骤弹层：处理战斗中的【阻挡】决策
 *
 * 服务端在 BattleBlock 阶段会停下等待防守方决策，并把 phase + battle 快照推回。
 * 防守方（currentTurn === false）选一张活跃的【阻挡者】角色顶替被攻击目标，或不阻挡。
 *
 * 注：反击步骤（BattleCounter）不在此处理——改为直接在手牌上渲染反击值、
 * 点击即丢弃加反击（见 HandArea），结束反击由右侧操作区按钮触发（见 GameActions）。
 */
export default function BattleDefenseOverlay() {
  const phase = useGameStore((s) => s.phase);
  const battle = useGameStore((s) => s.battle);
  const my = useGameStore((s) => s.my);
  const opp = useGameStore((s) => s.opponent);
  const isPending = useGameStore((s) => s.isPending);
  const isDefender = useIsDefender();

  // 仅防守方、战斗进行中、且处于阻挡步骤时显示
  // 用 isDefender（攻击者属于对手）而非 !currentTurn，兼容 GM「对手领袖攻击」场景
  if (!battle || !isDefender || phase !== "Block" || !my || !opp) return null;

  // 攻击者在对手方
  const attackerName =
    battle.attackerCardId === opp.leaderId
      ? getCard(opp.leaderNumber)?.name ?? "领袖"
      : getCard(opp.fieldCards.find((c) => c.id === battle.attackerCardId)?.number ?? "")?.name ?? "角色";
  const attackerPowerBase =
    battle.attackerCardId === opp.leaderId
      ? opp.leaderPower
      : opp.fieldCards.find((c) => c.id === battle.attackerCardId)?.powerCurrent ?? 0;
  const attackerPower = attackerPowerBase + battle.attackerBonus;

  // 被攻击目标在我方
  const targetName = battle.targetIsLeader
    ? getCard(my.leaderNumber)?.name ?? "领袖"
    : getCard(my.fieldCards.find((c) => c.id === battle.targetCardId)?.number ?? "")?.name ?? "角色";
  const targetPowerBase = battle.targetIsLeader
    ? my.leaderPower
    : my.fieldCards.find((c) => c.id === battle.targetCardId)?.powerCurrent ?? 0;
  const targetPower = targetPowerBase + battle.defenderBonus;

  const willLose = attackerPower >= targetPower;

  // 可用的【阻挡者】角色：未横置 + 带【阻挡者】关键字
  const hasBlockerKeyword = (number: string, gained: string[]) => {
    const card = getCard(number);
    return (
      gained.includes("阻挡者") ||
      (card?.keyWords?.includes("阻挡者") ?? false) ||
      (card?.abilities?.includes("阻挡者") ?? false)
    );
  };
  const blockers = my.fieldCards.filter(
    (c) => !c.isTapped && hasBlockerKeyword(c.number, c.gainedKeywords),
  );

  return (
    <AnimatePresence>
      <motion.div
        className="fixed inset-x-0 bottom-0 z-50 flex flex-col items-center gap-3 border-t border-sky-200/20 bg-slate-950/95 px-6 py-4 shadow-2xl shadow-black/60"
        initial={{ y: "100%" }}
        animate={{ y: 0 }}
        exit={{ y: "100%" }}
        transition={{ type: "spring", stiffness: 240, damping: 28 }}
      >
        <div className="flex items-center gap-4">
          <span className="rounded-full bg-red-600/80 px-3 py-1 text-sm font-black text-white">
            阻挡步骤
          </span>
          <span className="text-sm font-bold text-slate-200">
            {attackerName} 攻击 {battle.targetIsLeader ? "你的领袖" : targetName}
          </span>
        </div>

        <div className="flex items-center gap-3 text-sm font-black">
          <span className="text-red-300">攻击 {attackerPower}</span>
          <span className="text-slate-500">vs</span>
          <span className="text-sky-300">防御 {targetPower}</span>
          <span className={willLose ? "text-red-400" : "text-green-400"}>
            {willLose ? "（当前会被击败）" : "（当前可挡住）"}
          </span>
        </div>

        <div className="flex flex-col items-center gap-2">
          {blockers.length > 0 ? (
            <div className="flex flex-wrap justify-center gap-2">
              {blockers.map((b) => (
                <button
                  key={b.id}
                  type="button"
                  disabled={isPending}
                  onClick={() => GameRequest.declareBlocker(b.id)}
                  className="rounded-md ring-2 ring-transparent transition hover:ring-amber-300 disabled:cursor-not-allowed disabled:opacity-50"
                >
                  <CardItem card={getCard(b.number) ?? null} size="sm" isTapped={b.isTapped} />
                </button>
              ))}
            </div>
          ) : (
            <span className="text-xs text-slate-400">没有可用的【阻挡者】</span>
          )}
          <button
            type="button"
            disabled={isPending}
            onClick={() => GameRequest.passBlock()}
            className="rounded-md bg-slate-700 px-6 py-2 text-sm font-bold text-white transition hover:bg-slate-600 disabled:cursor-not-allowed disabled:opacity-50"
          >
            不阻挡
          </button>
        </div>
      </motion.div>
    </AnimatePresence>
  );
}
