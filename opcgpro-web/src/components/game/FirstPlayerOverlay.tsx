"use client";

import { useEffect, useState } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { useGameStore } from "@/store/gameStore";
import { GameRequest } from "@/net/GameRequest";

const DIE_FACES = ["", "⚀", "⚁", "⚂", "⚃", "⚄", "⚅"];

function Die({ value, rolling, label }: { value: number; rolling: boolean; label: string }) {
  return (
    <div className="flex flex-col items-center gap-3">
      <span className="text-xs font-bold tracking-widest text-slate-400">{label}</span>
      <motion.div
        className="flex h-28 w-28 items-center justify-center rounded-2xl border border-white/25 bg-gradient-to-br from-white to-slate-200 text-7xl text-slate-950 shadow-2xl shadow-black/50"
        animate={rolling ? { rotate: 360, scale: 0.82 } : { rotate: 0, scale: 1 }}
        transition={rolling
          ? { duration: 0.35, repeat: Infinity, ease: "linear" }
          : { type: "spring", stiffness: 260, damping: 18 }}
      >
        {rolling ? "?" : DIE_FACES[value]}
      </motion.div>
      <span className="h-6 text-lg font-black text-white">{rolling ? "" : `${value} 点`}</span>
    </div>
  );
}

export default function FirstPlayerOverlay() {
  const my = useGameStore((s) => s.my);
  const opponent = useGameStore((s) => s.opponent);
  const firstPlayerChosen = useGameStore((s) => s.firstPlayerChosen);
  const canChooseFirstPlayer = useGameStore((s) => s.canChooseFirstPlayer);
  const diceWinnerIsMe = useGameStore((s) => s.diceWinnerIsMe);
  const startingDiceRolls = useGameStore((s) => s.startingDiceRolls);
  const isPending = useGameStore((s) => s.isPending);
  const [roundIndex, setRoundIndex] = useState(0);
  const [settled, setSettled] = useState(false);
  const [animationComplete, setAnimationComplete] = useState(false);

  useEffect(() => {
    if (firstPlayerChosen || startingDiceRolls.length === 0) return;

    let cancelled = false;
    const timers: number[] = [];
    const wait = (duration: number) => new Promise<void>((resolve) => {
      const timer = window.setTimeout(resolve, duration);
      timers.push(timer);
    });

    const playRolls = async () => {
      setAnimationComplete(false);
      for (let index = 0; index < startingDiceRolls.length; index++) {
        if (cancelled) return;
        setRoundIndex(index);
        setSettled(false);
        await wait(700);
        if (cancelled) return;
        setSettled(true);

        if (startingDiceRolls[index].tie) {
          await wait(1100);
          if (cancelled) return;
        } else {
          setAnimationComplete(true);
          return;
        }
      }
    };

    void playRolls();
    return () => {
      cancelled = true;
      timers.forEach((timer) => window.clearTimeout(timer));
    };
  }, [firstPlayerChosen, startingDiceRolls]);

  if (!my || firstPlayerChosen) return null;

  const currentRoll = startingDiceRolls[roundIndex];
  const isTie = settled && currentRoll?.tie;

  return (
    <AnimatePresence>
      <motion.div
        className="fixed inset-0 z-[55] flex flex-col items-center justify-center gap-7 bg-slate-950/95 px-4"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
      >
        <div className="text-center">
          <p className="text-2xl font-black tracking-wide text-white">投骰决定选择权</p>
          <p className="mt-2 text-sm text-slate-400">
            {startingDiceRolls.length > 0 ? `第 ${roundIndex + 1} 次投掷` : "正在准备骰子..."}
          </p>
        </div>

        {currentRoll && (
          <div className="flex items-center gap-10 sm:gap-16">
            <Die value={currentRoll.my} rolling={!settled} label={my.name || "我方"} />
            <span className="mt-4 text-lg font-black text-slate-500">VS</span>
            <Die value={currentRoll.opponent} rolling={!settled} label={opponent?.name || "对手"} />
          </div>
        )}

        <div className="flex min-h-24 flex-col items-center justify-start gap-4 text-center">
          {!settled && currentRoll && (
            <p className="text-base font-bold text-sky-300">双方正在投掷六面骰...</p>
          )}
          {isTie && (
            <motion.p
              className="text-base font-bold text-amber-300"
              initial={{ opacity: 0, y: 8 }}
              animate={{ opacity: 1, y: 0 }}
            >
              点数相同，即将重新投掷
            </motion.p>
          )}
          {animationComplete && !isTie && (
            <>
              <p className={`text-lg font-black ${diceWinnerIsMe ? "text-emerald-300" : "text-orange-300"}`}>
                {diceWinnerIsMe ? "你的点数更大，请选择先后手" : "对手点数更大，获得先后手选择权"}
              </p>
              {canChooseFirstPlayer ? (
                <div className="flex gap-4">
                  <button
                    type="button"
                    disabled={isPending}
                    onClick={() => GameRequest.chooseFirstPlayer(true)}
                    className="rounded-xl bg-orange-500 px-8 py-3 text-base font-black text-white transition-colors hover:bg-orange-400 disabled:cursor-wait disabled:opacity-50"
                  >
                    选择先手
                  </button>
                  <button
                    type="button"
                    disabled={isPending}
                    onClick={() => GameRequest.chooseFirstPlayer(false)}
                    className="rounded-xl bg-sky-600 px-8 py-3 text-base font-black text-white transition-colors hover:bg-sky-500 disabled:cursor-wait disabled:opacity-50"
                  >
                    选择后手
                  </button>
                </div>
              ) : (
                <div className="flex items-center gap-3 text-sm text-slate-300">
                  <div className="h-5 w-5 animate-spin rounded-full border-2 border-white/30 border-t-white" />
                  等待对手选择先后手...
                </div>
              )}
            </>
          )}
        </div>
      </motion.div>
    </AnimatePresence>
  );
}
