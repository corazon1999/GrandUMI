"use client";

import { useEffect, useState } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { useGameStore } from "@/store/gameStore";
import { GameRequest } from "@/net/GameRequest";
import { getGameCard } from "@/data/CardLoader";
import { advanceImageFallback, CARD_BACK_SRC, thumbSrc } from "@/lib/sprite";

const DIE_FACES = ["", "⚀", "⚁", "⚂", "⚃", "⚄", "⚅"];
const RECOVERY_RETRY_INTERVAL_MS = 2500;
const MAX_RECOVERY_ATTEMPTS = 3;

function Die({ value, rolling, label }: { value: number; rolling: boolean; label: string }) {
  return (
    <div className="flex shrink-0 flex-col items-center gap-2 sm:gap-3">
      <span className="text-xs font-bold tracking-widest text-slate-400">{label}</span>
      <motion.div
        className="flex h-[clamp(76px,9vw,112px)] w-[clamp(76px,9vw,112px)] items-center justify-center rounded-2xl border border-white/25 bg-gradient-to-br from-white to-slate-200 text-[clamp(48px,6vw,72px)] text-slate-950 shadow-2xl shadow-black/50"
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

function LeaderPreview({
  leaderNumber,
  side,
}: {
  leaderNumber: string;
  side: "my" | "opponent";
}) {
  const spriteMap = useGameStore((state) =>
    side === "my" ? state.my?.spriteMap : state.opponent?.spriteMap,
  );
  const card = getGameCard(leaderNumber, spriteMap);
  const isMine = side === "my";
  const rawSprite = card?.sprite || CARD_BACK_SRC;

  return (
    <div className="flex min-w-0 flex-col items-center gap-1.5">
      <span className={`text-[10px] font-black tracking-[0.18em] ${isMine ? "text-cyan-200" : "text-orange-200"}`}>
        {isMine ? "我方领袖" : "对方领袖"}
      </span>
      <div
        className={`relative aspect-[5/7] w-[clamp(68px,8vw,108px)] overflow-hidden rounded-lg border-2 bg-slate-900 shadow-xl ${
          isMine
            ? "border-cyan-300/75 shadow-cyan-500/20"
            : "border-orange-300/75 shadow-orange-500/20"
        }`}
      >
        <img
          src={thumbSrc(rawSprite)}
          alt={`${card?.name || leaderNumber || (isMine ? "我方" : "对方")}领袖卡图`}
          className="absolute inset-0 h-full w-full object-cover"
          draggable={false}
          onError={(event) => {
            advanceImageFallback(event.currentTarget, [rawSprite, card?.image]);
          }}
        />
      </div>
      <div className="max-w-[clamp(90px,12vw,150px)] text-center leading-tight">
        <p className="truncate text-[clamp(10px,1vw,13px)] font-black text-white">
          {card?.name || leaderNumber || "未知领袖"}
        </p>
        <p className="mt-0.5 text-[9px] font-bold tracking-wider text-white/50">{leaderNumber}</p>
      </div>
    </div>
  );
}

export default function FirstPlayerOverlay() {
  const my = useGameStore((s) => s.my);
  const opponent = useGameStore((s) => s.opponent);
  const firstPlayerChosen = useGameStore((s) => s.firstPlayerChosen);
  const canChooseFirstPlayer = useGameStore((s) => s.canChooseFirstPlayer);
  const diceWinnerIsMe = useGameStore((s) => s.diceWinnerIsMe);
  const startingPlayerChoiceDeadlineUtc = useGameStore((s) => s.startingPlayerChoiceDeadlineUtc);
  const startingDiceRolls = useGameStore((s) => s.startingDiceRolls);
  const isPending = useGameStore((s) => s.isPending);
  const [roundIndex, setRoundIndex] = useState(0);
  const [settled, setSettled] = useState(false);
  const [animationComplete, setAnimationComplete] = useState(false);
  const [now, setNow] = useState(() => Date.now());

  const deadlineMs = startingPlayerChoiceDeadlineUtc
    ? Date.parse(startingPlayerChoiceDeadlineUtc)
    : Number.NaN;
  const remainingSeconds = Number.isFinite(deadlineMs)
    ? Math.max(0, Math.ceil((deadlineMs - now) / 1000))
    : 60;
  const isExpiring = remainingSeconds <= 10;
  const timedOut = remainingSeconds === 0;

  useEffect(() => {
    if (!startingPlayerChoiceDeadlineUtc || firstPlayerChosen) return;
    setNow(Date.now());
    const timer = window.setInterval(() => setNow(Date.now()), 250);
    return () => window.clearInterval(timer);
  }, [firstPlayerChosen, startingPlayerChoiceDeadlineUtc]);

  useEffect(() => {
    if (!timedOut || firstPlayerChosen || !startingPlayerChoiceDeadlineUtc) return;

    let attempts = 0;
    const requestRecovery = () => {
      attempts += 1;
      GameRequest.requestState();
    };

    requestRecovery();
    const timer = window.setInterval(() => {
      if (attempts >= MAX_RECOVERY_ATTEMPTS) {
        window.clearInterval(timer);
        return;
      }
      requestRecovery();
    }, RECOVERY_RETRY_INTERVAL_MS);

    return () => window.clearInterval(timer);
  }, [firstPlayerChosen, startingPlayerChoiceDeadlineUtc, timedOut]);

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
        className="fixed inset-0 z-[55] flex flex-col items-center justify-center gap-[clamp(12px,3vh,28px)] bg-slate-950/95 px-3 py-2 sm:px-4"
        style={{
          paddingTop: "calc(0.5rem + var(--layout-safe-top, env(safe-area-inset-top)))",
          paddingRight: "calc(0.75rem + var(--layout-safe-right, env(safe-area-inset-right)))",
          paddingBottom: "calc(0.5rem + var(--layout-safe-bottom, env(safe-area-inset-bottom)))",
          paddingLeft: "calc(0.75rem + var(--layout-safe-left, env(safe-area-inset-left)))",
        }}
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
          <div className="grid w-full max-w-5xl grid-cols-[1fr_auto_1fr] items-center gap-[clamp(10px,3vw,48px)]">
            <div className="flex min-w-0 items-center justify-end gap-[clamp(8px,2vw,24px)]">
              <LeaderPreview leaderNumber={my.leaderNumber} side="my" />
              <Die value={currentRoll.my} rolling={!settled} label={my.name || "我方"} />
            </div>
            <span className="text-base font-black text-slate-500 sm:text-lg">VS</span>
            <div className="flex min-w-0 items-center gap-[clamp(8px,2vw,24px)]">
              <Die value={currentRoll.opponent} rolling={!settled} label={opponent?.name || "对手"} />
              <LeaderPreview leaderNumber={opponent?.leaderNumber ?? ""} side="opponent" />
            </div>
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
              <p className={`text-sm font-black tabular-nums ${isExpiring ? "text-rose-300" : "text-amber-200"}`}>
                选择剩余 {remainingSeconds} 秒
              </p>
              {canChooseFirstPlayer && !timedOut ? (
                <div className="flex gap-4">
                  <button
                    type="button"
                    disabled={isPending}
                    onClick={() => GameRequest.chooseFirstPlayer(true)}
                    className="min-h-11 rounded-xl bg-orange-500 px-8 py-3 text-base font-black text-white transition-colors hover:bg-orange-400 disabled:cursor-wait disabled:opacity-50"
                  >
                    选择先手
                  </button>
                  <button
                    type="button"
                    disabled={isPending}
                    onClick={() => GameRequest.chooseFirstPlayer(false)}
                    className="min-h-11 rounded-xl bg-sky-600 px-8 py-3 text-base font-black text-white transition-colors hover:bg-sky-500 disabled:cursor-wait disabled:opacity-50"
                  >
                    选择后手
                  </button>
                </div>
              ) : (
                <div className="flex items-center gap-3 text-sm text-slate-300">
                  <div className="h-5 w-5 animate-spin rounded-full border-2 border-white/30 border-t-white" />
                  {timedOut
                    ? "时间到，正在同步自动选择结果..."
                    : canChooseFirstPlayer
                      ? "正在提交选择..."
                      : "等待对手选择先后手..."}
                </div>
              )}
              <p className="text-xs text-slate-500">超时后将默认由骰点胜者先手</p>
            </>
          )}
        </div>
      </motion.div>
    </AnimatePresence>
  );
}
