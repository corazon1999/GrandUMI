"use client";

import { useState, useEffect, useRef } from "react";
import HandArea from "@/components/game/HandArea";
import FieldArea from "@/components/game/FieldArea";
import LeaderCard from "@/components/game/LeaderCard";
import LifeArea from "@/components/game/LifeArea";
import DonArea from "@/components/game/DonArea";
import DonDeckPile from "@/components/game/DonDeckPile";
import DeckPile from "@/components/game/DeckPile";
import TrashPile from "@/components/game/TrashPile";
import StageSlot from "@/components/game/StageSlot";
import GameLog from "@/components/game/GameLog";
import GameActions from "@/components/game/GameActions";
import AnimationLayer from "@/components/game/AnimationLayer";
import BattleRelationLayer from "@/components/game/BattleRelationLayer";
import RevealOverlay from "@/components/game/RevealOverlay";
import GameChatPanel from "@/components/game/GameChatPanel";
import { useGameStore } from "@/store/gameStore";
import { useStageScale } from "@/hooks/useStageScale";
import { CardSizeOverride } from "@/hooks/useResponsive";
import { PHASE_LABELS } from "@/game/battle/BattlePhase";

// 对战页固定设计画布尺寸：内容按此基准布局，整体等比缩放铺满视口
const STAGE_W = 1280;
const STAGE_H = 720;

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
        "relative min-h-0 min-w-0 flex-1 rounded-md border border-sky-200/15 shadow-inner shadow-black/35",
        isOpponent ? "bg-red-950/[0.07]" : "bg-sky-950/[0.16]",
      ].join(" ")}
    >
      <div className={`absolute inset-x-0 ${isOpponent ? "bottom-0 h-px bg-red-300/20" : "top-0 h-px bg-sky-300/20"}`} />

      <div className={`absolute right-3 z-20 flex flex-col gap-2 ${isOpponent ? "top-1/2 -translate-y-1/2" : "top-[45%] -translate-y-1/2"}`}>
        <DeckPile side={side} />
        <TrashPile side={side} />
      </div>

      <div className="grid h-full min-w-0 grid-rows-[1fr_auto_1fr] gap-2 p-3 pr-32 md:pr-36">
        {isOpponent ? (
          <div className="relative min-h-0 min-w-0 pl-24 md:pl-28">
            <HandArea side={side} hidden />
          </div>
        ) : (
          <div className="relative flex min-h-0 min-w-0 items-stretch gap-4 pl-24 md:pl-28">
            <div className="absolute left-0 top-0 z-20">
              <LifeArea side={side} />
            </div>
            {fieldZone}
          </div>
        )}

        <div className="grid min-h-0 min-w-0 grid-cols-[minmax(14rem,0.9fr)_minmax(16rem,1.1fr)] items-center gap-4">
          <div className="min-w-0">{donZone}</div>
          <div className="justify-self-center">{leaderStage}</div>
        </div>

        {isOpponent ? (
          <div className="relative flex min-h-0 min-w-0 items-stretch gap-4 pl-24 md:pl-28">
            <div className="absolute left-0 top-0 z-20">
              <LifeArea side={side} />
            </div>
            {fieldZone}
          </div>
        ) : (
          <div className="relative -ml-[233px] min-h-0 min-w-0">
            <HandArea side={side} hidden={isObserver} />
          </div>
        )}
      </div>
    </section>
  );
}

// 回合五阶段（平时显示）
const TURN_FLOW = ["Reset", "Draw", "Don", "Main", "End"];
// 战斗子流程（攻击宣言后显示后端真实的 4 个步骤）
const BATTLE_FLOW = ["Attack", "Block", "Counter", "Damage"];
const BATTLE_PHASES = new Set(BATTLE_FLOW);
// #238 每回合倒计时基准秒数（纯前端提示，不判负；后端无权威计时）
const TURN_SECONDS = 300;

// #238 回合倒计时徽章：每次 turnCount 变化即从 TURN_SECONDS 重新倒数；仅对局中（live）走秒
function TurnTimer({ turnCount, live }: { turnCount: number; live: boolean }) {
  const [remain, setRemain] = useState(TURN_SECONDS);

  // 回合切换即重置（turnCount 由后端权威快照驱动，天然与实际回合对齐）
  useEffect(() => {
    setRemain(TURN_SECONDS);
  }, [turnCount]);

  useEffect(() => {
    if (!live) return;
    const id = setInterval(() => {
      setRemain((r) => (r > 0 ? r - 1 : 0));
    }, 1000);
    return () => clearInterval(id);
  }, [live, turnCount]);

  const mm = Math.floor(remain / 60);
  const ss = remain % 60;
  const low = remain <= 30;
  return (
    <span
      className={[
        "shrink-0 rounded-md px-2 py-1 font-mono text-[11px] font-black tabular-nums",
        low
          ? "bg-red-500/25 text-red-200 ring-1 ring-red-400/50"
          : "bg-slate-700/50 text-slate-200 ring-1 ring-white/10",
      ].join(" ")}
      title="本回合计时（前端提示，不判负）"
    >
      ⏱ {mm}:{ss.toString().padStart(2, "0")}
    </span>
  );
}

function PhaseTrack({
  currentTurn,
  phase,
  turnCount,
  live,
}: {
  currentTurn: boolean;
  phase: string;
  turnCount: number;
  live: boolean;
}) {
  const inBattle = BATTLE_PHASES.has(phase);
  const flow = inBattle ? BATTLE_FLOW : TURN_FLOW;
  return (
    <div className="flex shrink-0 items-center justify-center gap-2 py-0.5">
      <TurnTimer turnCount={turnCount} live={live} />
      <span
        className={[
          "shrink-0 rounded-md px-2.5 py-1 text-[11px] font-black",
          currentTurn
            ? "bg-sky-500/20 text-sky-200 ring-1 ring-sky-400/40"
            : "bg-red-500/20 text-red-200 ring-1 ring-red-400/40",
        ].join(" ")}
      >
        {currentTurn ? "我的回合" : "对手回合"}
        {inBattle ? " · 战斗中" : ""}
      </span>
      <div className="flex items-center gap-1.5">
        {flow.map((p) => {
          const isActive = p === phase;
          return (
            <div
              key={p}
              className={[
                "rounded-md px-2.5 py-1 text-[11px] font-black transition-colors",
                isActive
                  ? currentTurn
                    ? "bg-sky-500 text-white shadow shadow-sky-500/40"
                    : "bg-red-500 text-white shadow shadow-red-500/40"
                  : "border border-white/10 bg-slate-800/60 text-slate-400",
              ].join(" ")}
            >
              {PHASE_LABELS[p] ?? p}
            </div>
          );
        })}
      </div>
    </div>
  );
}

function LeftRail() {
  return (
    <aside className="flex h-full min-h-0 w-52 shrink-0 flex-col gap-3 pb-28">
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
  myName,
  opponentName,
  isObserver,
  isPlayback,
}: {
  myName: string;
  opponentName: string;
  isObserver: boolean;
  isPlayback: boolean;
}) {
  return (
    <aside className="relative z-40 flex h-full min-h-0 w-44 shrink-0 flex-col gap-3">
      <section className="rounded-md border border-sky-200/15 bg-slate-950/65 p-3 shadow-inner shadow-black/30">
        <p className="text-xs font-black text-slate-300">对手</p>
        <p className="mt-1 truncate text-sm font-black text-white">{opponentName || "对手"}</p>
        <div className="my-3 h-px bg-white/10" />
        <p className="text-xs font-black text-slate-300">我</p>
        <p className="mt-1 truncate text-sm font-black text-sky-100">{myName || "我"}</p>
      </section>
      {!isPlayback && (
        <>
          <section className="relative min-h-0 flex-1 overflow-y-auto rounded-md border border-sky-200/15 bg-slate-950/65 p-3 shadow-inner shadow-black/30">
            <h2 className="text-xs font-black text-slate-300">操作日志</h2>
            <GameLog />
          </section>
          {!isObserver && (
            <section className="rounded-md border border-sky-200/15 bg-slate-950/65 p-3 shadow-inner shadow-black/30">
              <h2 className="mb-2 text-xs font-black text-slate-300">操作</h2>
              <GameActions />
            </section>
          )}
        </>
      )}
      {isPlayback && (
        <section className="relative min-h-0 flex-1 overflow-y-auto rounded-md border border-sky-200/15 bg-slate-950/65 p-3 shadow-inner shadow-black/30">
          <h2 className="text-xs font-black text-slate-300">操作日志</h2>
          <GameLog />
        </section>
      )}
    </aside>
  );
}

/**
 * GameBoard — 牌桌渲染（对战页与回放页共用）
 *
 * 纯展示层：从 gameStore 读取镜像状态并按固定 1280×720 画布 scale-to-fit 渲染。
 * 不含玩家专属浮层（轮抽/Prompt/菜单等）与结算弹窗——那些由各页面自行叠加。
 * 回放页传 isPlayback 即可复用同一套牌桌。
 */
export default function GameBoard({
  isObserver,
  isPlayback,
}: {
  isObserver: boolean;
  isPlayback: boolean;
}) {
  const currentTurn = useGameStore((s) => s.currentTurn);
  const phase = useGameStore((s) => s.phase);
  const turnCount = useGameStore((s) => s.turnCount);
  const myName = useGameStore((s) => s.myName);
  const opponentName = useGameStore((s) => s.opponentName);

  const stageScale = useStageScale(STAGE_W, STAGE_H);
  const stageRef = useRef<HTMLDivElement>(null);

  return (
    <>
      <div className="absolute inset-0 bg-[radial-gradient(circle_at_center,_rgba(33,92,145,0.26),_transparent_62%),linear-gradient(135deg,_#0b1a2c,_#07111f_48%,_#0a1524)]" />
      <div className="absolute inset-0 opacity-[0.08] [background-image:linear-gradient(rgba(255,255,255,.6)_1px,transparent_1px),linear-gradient(90deg,rgba(255,255,255,.6)_1px,transparent_1px)] [background-size:18px_18px]" />

      <AnimationLayer />
      <RevealOverlay />

      {/* 固定设计画布 + 整体等比缩放居中（scale-to-fit），保证任何宽高比下比例恒定、不裁切 */}
      <div className="absolute inset-0 z-10 flex items-center justify-center">
        <CardSizeOverride.Provider value="sm">
          <div
            ref={stageRef}
            className="relative shrink-0"
            style={{
              width: STAGE_W,
              height: STAGE_H,
              transform: `scale(${stageScale})`,
              transformOrigin: "center",
            }}
          >
            <BattleRelationLayer />
            <div className="absolute inset-3 flex gap-3">
              <LeftRail />

              <main className="relative z-0 flex min-w-0 flex-1 flex-col gap-2">
                <PlayerMat side="opponent" isObserver={isObserver} isPlayback={isPlayback} />

                <PhaseTrack
                  currentTurn={currentTurn}
                  phase={phase}
                  turnCount={turnCount}
                  live={!isObserver && !isPlayback}
                />

                <PlayerMat side="my" isObserver={isObserver} isPlayback={isPlayback} />
              </main>

              <RightRail
                myName={myName}
                opponentName={opponentName}
                isObserver={isObserver}
                isPlayback={isPlayback}
              />
            </div>

          </div>
        </CardSizeOverride.Provider>
      </div>

      {/* 局内聊天（固定屏幕角，不随画布缩放；回放模式内部自隐） */}
      <GameChatPanel isPlayback={isPlayback} />
    </>
  );
}
