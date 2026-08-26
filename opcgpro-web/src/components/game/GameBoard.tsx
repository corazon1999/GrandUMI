"use client";

import { useState, useEffect, useRef, type MouseEvent as ReactMouseEvent } from "react";
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
import EffectActivationLayer from "@/components/game/EffectActivationLayer";
import CardZoneTransitionLayer from "@/components/game/CardZoneTransitionLayer";
import RevealOverlay from "@/components/game/RevealOverlay";
import GameChatPanel from "@/components/game/GameChatPanel";
import { useGameStore } from "@/store/gameStore";
import { elapsedMillisecondsFromServerSync } from "@/lib/serverCountdown.mjs";
import { useStageScale } from "@/hooks/useStageScale";
import { CardSizeOverride } from "@/hooks/useResponsive";
import { PHASE_LABELS } from "@/game/battle/BattlePhase";
import { LeaderChampionBadge } from "@/components/ui/LeaderChampionBadge";
import type { PlayerRankIdentitySnapshot, RankFaction } from "@/types/net";
import { GameRequest } from "@/net/GameRequest";
import { useLayoutQuarterTurn } from "@/components/ui/ResponsiveScope";
import TurnExtensionIcon from "@/components/game/TurnExtensionIcon";

// 对战页固定设计画布尺寸：内容按此基准布局，整体等比缩放铺满视口
const STAGE_W = 1280;
const STAGE_H = 720;

function PlayerMat({
  side,
  isObserver,
  isPlayback,
  revealHands,
  revealObserverHand = false,
}: {
  side: "my" | "opponent";
  isObserver: boolean;
  isPlayback: boolean;
  revealHands: boolean;
  revealObserverHand?: boolean;
}) {
  const isOpponent = side === "opponent";
  const leaderStage = (
    <div className="flex items-end justify-center gap-4 md:gap-5">
      <LeaderCard side={side} />
      <StageSlot side={side} />
    </div>
  );
  // 回放快照同样携带双方费用区与咚卡组数量；保持可见才能完整复盘出牌和贴咚。
  const donZone = (
    <div className="flex w-full max-w-[32rem] items-center gap-2">
      <DonDeckPile side={side} />
      <div className="min-w-0 flex-1">
        <DonArea side={side} />
      </div>
    </div>
  );
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
            <HandArea side={side} hidden={!isPlayback && !revealHands} />
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
            <HandArea side={side} hidden={isObserver && !revealHands && !revealObserverHand} />
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

function PhaseTrack({
  currentTurn,
  phase,
}: {
  currentTurn: boolean;
  phase: string;
}) {
  const inBattle = BATTLE_PHASES.has(phase);
  const flow = inBattle ? BATTLE_FLOW : TURN_FLOW;
  return (
    <div className="flex shrink-0 items-center justify-center gap-2 py-0.5">
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
    <aside className="flex h-full min-h-0 w-52 shrink-0 flex-col pb-28">
      <section className="relative min-h-0 flex-1 overflow-y-auto rounded-md border border-sky-200/15 bg-slate-950/55 p-3 shadow-inner shadow-black/30">
        <h2 className="text-xs font-black text-slate-300">对战日志</h2>
        <GameLog />
      </section>
    </aside>
  );
}

const RANK_FACTION_NAMES: Record<RankFaction, string> = {
  pirate: "海贼",
  marine: "海军",
  government: "世界政府",
};

function rankTierLabel(rank: PlayerRankIdentitySnapshot): string {
  if (rank.placementGames < rank.placementRequired) {
    return `定级 ${rank.placementGames}/${rank.placementRequired}`;
  }
  return `${rank.tier}${rank.division ? ` ${["", "I", "II", "III"][rank.division]}` : ""}`;
}

function PlayerRankIdentity({ rank }: { rank?: PlayerRankIdentitySnapshot | null }) {
  if (!rank) return null;
  const label = `${RANK_FACTION_NAMES[rank.faction]} · ${rankTierLabel(rank)}`;
  return (
    <p
      className="mt-0.5 truncate text-[10px] font-bold leading-4 text-violet-200"
      title={label}
      aria-label={`排位身份：${label}`}
    >
      {label}
    </p>
  );
}

function RightRail({
  myName,
  opponentName,
  myRankIdentity,
  opponentRankIdentity,
  myChampionLeaderNumber,
  opponentChampionLeaderNumber,
  isObserver,
  isPlayback,
}: {
  myName: string;
  opponentName: string;
  myRankIdentity?: PlayerRankIdentitySnapshot | null;
  opponentRankIdentity?: PlayerRankIdentitySnapshot | null;
  myChampionLeaderNumber?: string | null;
  opponentChampionLeaderNumber?: string | null;
  isObserver: boolean;
  isPlayback: boolean;
}) {
  return (
    <aside
      data-game-right-rail
      className="relative z-40 flex h-full min-h-0 w-44 shrink-0 flex-col gap-3"
    >
      <section className="rounded-md border border-sky-200/15 bg-slate-950/65 p-3 shadow-inner shadow-black/30">
        <p className="text-xs font-black text-slate-300">对手</p>
        <p className="mt-1 truncate text-sm font-black text-white">{opponentName || "对手"}</p>
        <PlayerRankIdentity rank={opponentRankIdentity} />
        <LeaderChampionBadge leaderNumber={opponentChampionLeaderNumber} className="mt-1" />
        <OperationClock side="opponent" allowExtension={false} />
        <div className="my-3 h-px bg-white/10" />
        <p className="text-xs font-black text-slate-300">我</p>
        <p className="mt-1 truncate text-sm font-black text-sky-100">{myName || "我"}</p>
        <PlayerRankIdentity rank={myRankIdentity} />
        <LeaderChampionBadge leaderNumber={myChampionLeaderNumber} className="mt-1" />
        <OperationClock side="my" allowExtension={!isObserver && !isPlayback} />
      </section>
      <div className="mt-auto flex flex-col gap-3">
        {!isObserver && !isPlayback && (
          <section
            data-game-actions-panel
            className="rounded-md border border-sky-200/15 bg-slate-950/65 p-3 shadow-inner shadow-black/30"
          >
            <h2 className="mb-2 text-xs font-black text-slate-300">操作</h2>
            <GameActions />
          </section>
        )}
      </div>
    </aside>
  );
}

function formatOperationTime(milliseconds: number): string {
  const safe = Math.max(0, milliseconds);
  const minutes = Math.floor(safe / 60_000);
  const seconds = Math.floor((safe % 60_000) / 1000);
  if (safe < 10_000) return `${minutes}:${String(seconds).padStart(2, "0")}.${Math.floor((safe % 1000) / 100)}`;
  return `${minutes}:${String(seconds).padStart(2, "0")}`;
}

function monotonicNow(): number {
  return typeof performance === "undefined" ? 0 : performance.now();
}

function OperationClock({
  side,
  allowExtension,
}: {
  side: "my" | "opponent";
  allowExtension: boolean;
}) {
  const rotateQuarterTurn = useLayoutQuarterTurn();
  const enabled = useGameStore((s) => s.operationClockEnabled);
  const totalBase = useGameStore((s) => side === "my" ? s.myOperationTimeMs : s.opponentOperationTimeMs);
  const turnBase = useGameStore((s) => side === "my" ? s.myTurnOperationTimeMs : s.opponentTurnOperationTimeMs);
  const active = useGameStore((s) => s.operationClockActive);
  const syncUtc = useGameStore((s) => s.operationClockSyncUtc);
  const serverNowUtc = useGameStore((s) => s.serverNowUtc);
  const paused = useGameStore((s) => s.operationClockPaused);
  const extensionUsed = useGameStore((s) => side === "my" ? s.myTurnExtensionUsed : s.opponentTurnExtensionUsed);
  const matchKind = useGameStore((s) => s.matchKind);
  const [anchor, setAnchor] = useState(() => ({ syncUtc, serverNowUtc, receivedAt: monotonicNow() }));
  const [now, setNow] = useState(() => monotonicNow());

  useEffect(() => {
    const receivedAt = monotonicNow();
    setAnchor({ syncUtc, serverNowUtc, receivedAt });
    setNow(receivedAt);
  }, [serverNowUtc, syncUtc]);

  useEffect(() => {
    if (!enabled || active !== side || paused) return;
    setNow(monotonicNow());
    const timer = window.setInterval(() => setNow(monotonicNow()), Math.min(totalBase, turnBase) < 12_000 ? 100 : 500);
    return () => window.clearInterval(timer);
  }, [active, enabled, paused, side, syncUtc, totalBase, turnBase]);

  if (!enabled) return null;
  const anchorMatchesSnapshot = anchor.syncUtc === syncUtc && anchor.serverNowUtc === serverNowUtc;
  const elapsed = active === side && !paused && syncUtc
    ? elapsedMillisecondsFromServerSync(
        syncUtc,
        serverNowUtc,
        anchorMatchesSnapshot ? now - anchor.receivedAt : 0,
      )
    : 0;
  const totalRemaining = Math.max(0, totalBase - elapsed);
  const turnRemaining = Math.min(totalRemaining, Math.max(0, turnBase - elapsed));
  const urgent = Math.min(totalRemaining, turnRemaining) <= 60_000;
  return (
    <div className={`mt-1.5 grid grid-cols-[auto_1fr] items-center gap-x-2 rounded border px-2 py-1 font-mono font-black tabular-nums ${
      active === side && !paused
        ? urgent ? "border-red-400/70 bg-red-500/20 text-red-200" : "border-sky-400/60 bg-sky-500/15 text-sky-100"
        : "border-white/10 bg-black/20 text-slate-400"
    }`}>
      <span className="text-[9px] font-bold tracking-wide">{
        matchKind === "RankedWild" ? "狂野排位"
          : matchKind === "Ranked" ? "标准排位"
            : matchKind === "CasualWild" ? "狂野休闲"
              : matchKind === "CasualStandard" ? "标准休闲"
                : "休闲"
      }</span>
      <span className="justify-self-end text-sm" aria-label={`本回合剩余 ${formatOperationTime(turnRemaining)}`}>
        回合 {formatOperationTime(turnRemaining)}
      </span>
      <span className="col-span-2 justify-self-end text-[9px] font-bold opacity-75" aria-label={`总操作剩余 ${formatOperationTime(totalRemaining)}`}>
        总计 {formatOperationTime(totalRemaining)}
      </span>
      {allowExtension && !rotateQuarterTurn && side === "my" && active === "my" && !paused && !extensionUsed && (
        <button
          type="button"
          onClick={() => GameRequest.requestTurnExtension()}
          className="col-span-2 mt-1 flex h-11 w-11 min-h-11 min-w-11 items-center justify-center justify-self-end rounded-full border border-amber-300/50 bg-amber-400/15 text-amber-100 transition-colors hover:bg-amber-400/25 focus-visible:outline-2 focus-visible:outline-amber-200"
          aria-label="使用本局唯一一次回合加时，增加两分钟"
          title="回合加时 +2:00"
        >
          <TurnExtensionIcon />
        </button>
      )}
      {extensionUsed && (
        <span className="col-span-2 justify-self-end text-[9px] font-bold text-amber-200/75">加时已用</span>
      )}
    </div>
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
  onOpenFeedback,
}: {
  isObserver: boolean;
  isPlayback: boolean;
  onOpenFeedback?: () => void;
}) {
  const currentTurn = useGameStore((s) => s.currentTurn);
  const phase = useGameStore((s) => s.phase);
  const isGameOver = useGameStore((s) => s.isGameOver);
  const spectatorHandVisible = useGameStore((s) => s.spectatorHandVisible);
  const myName = useGameStore((s) => s.myName);
  const opponentName = useGameStore((s) => s.opponentName);
  const myRankIdentity = useGameStore((s) => s.my?.rankIdentity);
  const opponentRankIdentity = useGameStore((s) => s.opponent?.rankIdentity);
  const myChampionLeaderNumber = useGameStore((s) => s.my?.championLeaderNumber);
  const opponentChampionLeaderNumber = useGameStore((s) => s.opponent?.championLeaderNumber);
  const selectedDonIndex = useGameStore((s) => s.selectedDonIndex);
  const setSelectedDon = useGameStore((s) => s.setSelectedDon);

  const viewportRef = useRef<HTMLDivElement>(null);
  const stageScale = useStageScale(STAGE_W, STAGE_H, viewportRef);
  const stageRef = useRef<HTMLDivElement>(null);

  const handleBoardBlankClick = (event: ReactMouseEvent<HTMLDivElement>) => {
    if (selectedDonIndex === null || !(event.target instanceof Element)) return;

    // 卡牌、按钮等真实操作目标继续处理自身点击；只有牌桌空白区域取消待依附的咚。
    const interactiveTarget = event.target.closest(
      "button, a, input, textarea, select, [role='button'], [data-game-board-interactive='true']",
    );
    if (interactiveTarget) return;

    setSelectedDon(null);
  };

  return (
    <>
      <div className="absolute inset-0 bg-[radial-gradient(circle_at_center,_rgba(33,92,145,0.26),_transparent_62%),linear-gradient(135deg,_#0b1a2c,_#07111f_48%,_#0a1524)]" />
      <div className="absolute inset-0 opacity-[0.08] [background-image:linear-gradient(rgba(255,255,255,.6)_1px,transparent_1px),linear-gradient(90deg,rgba(255,255,255,.6)_1px,transparent_1px)] [background-size:18px_18px]" />

      <AnimationLayer />
      <RevealOverlay />

      {/* 固定设计画布 + 整体等比缩放居中（scale-to-fit），保证任何宽高比下比例恒定、不裁切 */}
      <div
        ref={viewportRef}
        className="absolute inset-0 z-10 flex items-center justify-center"
        onClick={handleBoardBlankClick}
      >
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
            <EffectActivationLayer />
            <CardZoneTransitionLayer />
            <div className="absolute inset-3 flex gap-3">
              <LeftRail />

              <main className="relative z-0 flex min-w-0 flex-1 flex-col gap-2">
                <PlayerMat
                  side="opponent"
                  isObserver={isObserver}
                  isPlayback={isPlayback}
                  revealHands={isGameOver}
                />

                <PhaseTrack
                  currentTurn={currentTurn}
                  phase={phase}
                />

                <PlayerMat
                  side="my"
                  isObserver={isObserver}
                  isPlayback={isPlayback}
                  revealHands={isGameOver}
                  revealObserverHand={isObserver && spectatorHandVisible}
                />
              </main>

              <RightRail
                myName={myName}
                opponentName={opponentName}
                myRankIdentity={myRankIdentity}
                opponentRankIdentity={opponentRankIdentity}
                myChampionLeaderNumber={myChampionLeaderNumber}
                opponentChampionLeaderNumber={opponentChampionLeaderNumber}
                isObserver={isObserver}
                isPlayback={isPlayback}
              />
            </div>

          </div>
        </CardSizeOverride.Provider>
      </div>

      {/* 局内聊天（固定屏幕角，不随画布缩放；回放模式内部自隐） */}
      <GameChatPanel
        isPlayback={isPlayback}
        isObserver={isObserver}
        onOpenFeedback={onOpenFeedback}
      />
    </>
  );
}
