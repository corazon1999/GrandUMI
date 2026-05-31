"use client";

import { useEffect, useMemo } from "react";
import { useRouter } from "next/navigation";
import { motion, AnimatePresence } from "framer-motion";
import HandArea from "@/components/game/HandArea";
import FieldArea from "@/components/game/FieldArea";
import LeaderCard from "@/components/game/LeaderCard";
import LifeArea from "@/components/game/LifeArea";
import DonArea from "@/components/game/DonArea";
import DonDeckPile from "@/components/game/DonDeckPile";
import DeckPile from "@/components/game/DeckPile";
import TrashPile from "@/components/game/TrashPile";
import StageSlot from "@/components/game/StageSlot";
import GameMenu from "@/components/game/GameMenu";
import ReconnectOverlay from "@/components/game/ReconnectOverlay";
import OpponentDisconnectBanner from "@/components/game/OpponentDisconnectBanner";
import AnimationLayer from "@/components/game/AnimationLayer";
import MulliganOverlay from "@/components/game/MulliganOverlay";
import PromptOverlay from "@/components/game/PromptOverlay";
import { useGameStore } from "@/store/gameStore";
import { usePlayback } from "@/hooks/usePlayback";
import { useGameInit } from "@/hooks/useGameInit";
import { PHASE_LABELS } from "@/game/battle/BattlePhase";

function PlayerMat({
  side,
  isObserver,
  isPlayback,
}: {
  side: "my" | "opponent";
  isObserver: boolean;
  isPlayback: boolean;
}) {
  const isOpponent = side === "opponent";
  const canShowDon = !isObserver && !isPlayback;
  const leaderStage = (
    <div className="flex items-end justify-center gap-4 md:gap-5">
      <LeaderCard side={side} />
      <StageSlot side={side} />
    </div>
  );
  const donZone = canShowDon ? (
    <div className="flex w-full max-w-[32rem] items-center gap-2">
      <DonDeckPile side={side} />
      <div className="min-w-0 flex-1">
        <DonArea side={side} />
      </div>
    </div>
  ) : null;
  const fieldZone = (
    <div className="min-w-0 flex-1 self-stretch">
      <FieldArea side={side} />
    </div>
  );

  return (
    <section
      className={[
        "relative min-h-0 flex-1 overflow-hidden rounded-md border border-sky-200/15 shadow-inner shadow-black/35",
        isOpponent ? "bg-red-950/[0.07]" : "bg-sky-950/[0.16]",
      ].join(" ")}
    >
      <div className={`absolute inset-x-0 ${isOpponent ? "bottom-0 h-px bg-red-300/20" : "top-0 h-px bg-sky-300/20"}`} />

      <div className={`absolute right-3 z-20 flex flex-col gap-2 ${isOpponent ? "top-1/2 -translate-y-1/2" : "top-[45%] -translate-y-1/2"}`}>
        <DeckPile side={side} />
        <TrashPile side={side} />
      </div>

      <div className="grid h-full grid-rows-3 gap-2 p-3 pr-32 md:pr-36">
        {isOpponent ? (
          <div className="relative min-h-0 pl-24 md:pl-28">
            <HandArea side={side} hidden />
          </div>
        ) : (
          <div className="relative flex min-h-0 items-stretch gap-4 pl-24 md:pl-28">
            <div className="absolute left-0 top-0 z-20">
              <LifeArea side={side} />
            </div>
            {fieldZone}
          </div>
        )}

        <div className="grid min-h-0 grid-cols-[minmax(14rem,0.9fr)_minmax(16rem,1.1fr)] items-center gap-4">
          <div className="min-w-0">{donZone}</div>
          <div className="justify-self-center">{leaderStage}</div>
        </div>

        {isOpponent ? (
          <div className="relative flex min-h-0 items-stretch gap-4 pl-24 md:pl-28">
            <div className="absolute left-0 top-0 z-20">
              <LifeArea side={side} />
            </div>
            {fieldZone}
          </div>
        ) : (
          <div className="relative min-h-0 pl-24 md:pl-28">
            <HandArea side={side} hidden={isObserver} />
          </div>
        )}
      </div>
    </section>
  );
}

function LeftRail() {
  return (
    <aside className="hidden h-full min-h-0 w-40 shrink-0 flex-col gap-3 md:flex xl:w-52">
      <section className="min-h-0 flex-1 rounded-md border border-sky-200/15 bg-slate-950/55 p-3 shadow-inner shadow-black/30">
        <h2 className="text-xs font-black text-slate-300">选中卡</h2>
        <div className="mt-3 aspect-[5/7] rounded-md border border-dashed border-slate-600/70 bg-black/20" />
      </section>
      <section className="h-36 rounded-md border border-sky-200/15 bg-slate-950/55 p-3 shadow-inner shadow-black/30 xl:h-44">
        <h2 className="text-xs font-black text-slate-300">记录</h2>
      </section>
    </aside>
  );
}

function RightRail({
  currentTurn,
  phase,
  myName,
  opponentName,
  isObserver,
  isPlayback,
}: {
  currentTurn: boolean;
  phase: string;
  myName: string;
  opponentName: string;
  isObserver: boolean;
  isPlayback: boolean;
}) {
  return (
    <aside className="hidden h-full min-h-0 w-36 shrink-0 flex-col gap-3 md:flex xl:w-44">
      <section className="rounded-md border border-sky-200/15 bg-slate-950/65 p-3 shadow-inner shadow-black/30">
        <p className="text-xs font-black text-slate-300">对手</p>
        <p className="mt-1 truncate text-sm font-black text-white">{opponentName || "对手"}</p>
        <div className="my-3 h-px bg-white/10" />
        <p className="text-xs font-black text-slate-300">我</p>
        <p className="mt-1 truncate text-sm font-black text-sky-100">{myName || "我"}</p>
      </section>
      <section className="rounded-md border border-sky-200/15 bg-slate-950/65 p-3 shadow-inner shadow-black/30">
        <p className="text-xs font-black text-slate-300">回合</p>
        <p className="mt-2 text-sm font-black text-white">{currentTurn ? "我的回合" : "对手回合"}</p>
        <p className="mt-3 text-xs font-black text-slate-300">阶段</p>
        <p className="mt-2 text-sm font-bold text-sky-100">{PHASE_LABELS[phase as keyof typeof PHASE_LABELS] ?? phase}</p>
      </section>
      {!isObserver && !isPlayback && (
        <section className="relative min-h-0 flex-1 rounded-md border border-sky-200/15 bg-slate-950/65 p-3 shadow-inner shadow-black/30">
          <h2 className="text-xs font-black text-slate-300">操作</h2>
          <GameMenu />
        </section>
      )}
    </aside>
  );
}

export default function GamePage() {
  const router = useRouter();
  const {
    mode,
    currentTurn,
    phase,
    myName,
    opponentName,
    isPending,
    isGameOver,
  } = useGameStore();

  const isObserver = mode === "Observer";
  const isPlayback = mode === "Playback";

  useGameInit();

  const playbackRecord = useMemo(() => {
    if (!isPlayback) return null;
    try {
      const raw = sessionStorage.getItem("grandumi_playback");
      return raw ? JSON.parse(raw) : null;
    } catch {
      return null;
    }
  }, [isPlayback]);

  const playback = usePlayback(playbackRecord);

  useEffect(() => {
    if (isPlayback && playback.state === "idle") {
      playback.play();
    }
  }, [isPlayback, playback]);

  return (
    <div className="relative h-screen w-screen overflow-hidden bg-[#07111f] text-white select-none">
      <div className="absolute inset-0 bg-[radial-gradient(circle_at_center,_rgba(33,92,145,0.26),_transparent_62%),linear-gradient(135deg,_#0b1a2c,_#07111f_48%,_#0a1524)]" />
      <div className="absolute inset-0 opacity-[0.08] [background-image:linear-gradient(rgba(255,255,255,.6)_1px,transparent_1px),linear-gradient(90deg,rgba(255,255,255,.6)_1px,transparent_1px)] [background-size:18px_18px]" />

      {!isObserver && !isPlayback && <ReconnectOverlay />}
      {!isObserver && !isPlayback && <OpponentDisconnectBanner />}
      <AnimationLayer />
      {!isObserver && !isPlayback && <MulliganOverlay />}
      {!isObserver && !isPlayback && <PromptOverlay />}

      {isObserver && (
        <div className="absolute left-4 top-4 z-20 rounded-full bg-purple-600/80 px-3 py-1 text-xs text-white">
          观战模式
        </div>
      )}

      {isPlayback && (
        <div className="absolute left-4 top-4 z-20 rounded-full bg-green-600/80 px-3 py-1 text-xs text-white">
          回放模式
        </div>
      )}

      {!isObserver && !isPlayback && (
        <AnimatePresence>
          {isPending && (
            <motion.div
              className="fixed inset-0 z-30 flex cursor-wait items-center justify-center bg-black/30 pointer-events-auto"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
            >
              <div className="h-8 w-8 animate-spin rounded-full border-3 border-white/40 border-t-white" />
            </motion.div>
          )}
        </AnimatePresence>
      )}

      <AnimatePresence>
        {isGameOver && (
          <motion.div
            className="fixed inset-0 z-40 flex flex-col items-center justify-center bg-black/70"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
          >
            <p className="mb-4 text-2xl font-bold text-white">游戏结束</p>
            <button
              onClick={() => {
                useGameStore.getState().resetGame();
                router.push("/home");
              }}
              className="rounded-lg bg-orange-500 px-6 py-2 text-white transition-colors hover:bg-orange-400"
            >
              返回大厅
            </button>
          </motion.div>
        )}
      </AnimatePresence>

      <div className="absolute inset-3 z-10 mx-auto flex max-w-[110rem] gap-3">
        <LeftRail />

        <main className="flex min-w-0 flex-1 flex-col gap-2">
          <PlayerMat
            side="opponent"
            isObserver={isObserver}
            isPlayback={isPlayback}
          />

          <PlayerMat
            side="my"
            isObserver={isObserver}
            isPlayback={isPlayback}
          />
        </main>

        <RightRail
          currentTurn={currentTurn}
          phase={phase}
          myName={myName}
          opponentName={opponentName}
          isObserver={isObserver}
          isPlayback={isPlayback}
        />
      </div>

      {!isObserver && !isPlayback && (
        <div className="md:hidden">
          <GameMenu />
        </div>
      )}
    </div>
  );
}
