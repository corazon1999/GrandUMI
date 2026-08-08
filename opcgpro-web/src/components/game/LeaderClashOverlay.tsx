"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { motion, useReducedMotion } from "framer-motion";
import { getCard } from "@/data/CardLoader";
import leaderIntroQuotes from "@/data/leaderIntroQuotes.json";
import { useGameStore } from "@/store/gameStore";
import { advanceImageFallback, CARD_BACK_SRC, displaySrc } from "@/lib/sprite";

type IntroPhase = "waiting" | "playing" | "exiting" | "done";

const IMPACT_SHARDS = [-78, -52, -29, -11, 14, 33, 57, 82, 108, 137, 164, 191];

interface LeaderIntroQuoteData {
  fallback: string;
  byName: Record<string, string>;
  byNumber: Record<string, string>;
}

const INTRO_QUOTES = leaderIntroQuotes as LeaderIntroQuoteData;

function getLeaderIntroQuote(leaderNumber: string, leaderName: string): string {
  return INTRO_QUOTES.byNumber[leaderNumber]
    ?? INTRO_QUOTES.byName[leaderName]
    ?? INTRO_QUOTES.fallback;
}

interface Props {
  ready: boolean;
  onComplete: () => void;
}

interface FighterCardProps {
  side: "left" | "right";
  playerName: string;
  leaderName: string;
  leaderNumber: string;
  quote: string;
  sprite: string;
  playing: boolean;
  reducedMotion: boolean;
}

function FighterCard({
  side,
  playerName,
  leaderName,
  leaderNumber,
  quote,
  sprite,
  playing,
  reducedMotion,
}: FighterCardProps) {
  const isLeft = side === "left";
  const offscreenX = isLeft ? "-72vw" : "72vw";
  const collisionX = isLeft ? "3.2vw" : "-3.2vw";
  const restX = isLeft ? "-1.2vw" : "1.2vw";
  const entryRotate = isLeft ? -12 : 12;
  const collisionRotate = isLeft ? 5 : -5;
  const restRotate = isLeft ? -2 : 2;

  return (
    <motion.article
      className={`relative z-10 flex flex-col ${isLeft ? "items-end text-right" : "items-start text-left"}`}
      initial={{
        x: reducedMotion ? (isLeft ? "-10vw" : "10vw") : offscreenX,
        rotate: entryRotate,
        scale: 0.86,
        opacity: 0,
      }}
      animate={playing
        ? {
            x: reducedMotion
              ? [isLeft ? "-10vw" : "10vw", restX]
              : [offscreenX, offscreenX, collisionX, restX],
            rotate: reducedMotion
              ? [entryRotate, restRotate]
              : [entryRotate, entryRotate, collisionRotate, restRotate],
            scale: reducedMotion ? [0.92, 1] : [0.86, 0.86, 1.08, 1],
            opacity: reducedMotion ? [0, 1] : [0, 0, 1, 1],
          }
        : undefined}
      transition={reducedMotion
        ? { duration: 0.32, ease: "easeOut" }
        : { duration: 1.55, times: [0, 0.1, 0.68, 1], ease: [0.22, 0.8, 0.2, 1] }}
    >
      <div
        className={`relative aspect-[5/7] w-[clamp(112px,min(21vw,32vh),250px)] overflow-hidden rounded-[clamp(8px,1vw,16px)] border-2 bg-slate-950 shadow-2xl ${
          isLeft
            ? "border-cyan-200/80 shadow-cyan-400/35"
            : "border-orange-200/80 shadow-orange-500/35"
        }`}
      >
        <img
          src={displaySrc(sprite)}
          alt={`${leaderName} Leader 卡图`}
          className="absolute inset-0 h-full w-full object-cover"
          draggable={false}
          onError={(event) => {
            advanceImageFallback(event.currentTarget, [sprite]);
          }}
        />
        <div
          className={`absolute inset-0 bg-gradient-to-t ${
            isLeft
              ? "from-cyan-950/85 via-transparent to-cyan-200/10"
              : "from-orange-950/85 via-transparent to-orange-200/10"
          }`}
        />
        <div
          className={`absolute inset-y-0 w-1/3 opacity-50 blur-xl ${
            isLeft ? "right-0 bg-cyan-300/40" : "left-0 bg-orange-300/40"
          }`}
        />
        <span
          className={`absolute top-3 rounded-full border px-3 py-1 text-[clamp(8px,1vw,11px)] font-black tracking-[0.2em] backdrop-blur-sm ${
            isLeft
              ? "left-3 border-cyan-100/40 bg-cyan-950/75 text-cyan-100"
              : "right-3 border-orange-100/40 bg-orange-950/75 text-orange-100"
          }`}
        >
          {isLeft ? "PLAYER 1" : "PLAYER 2"}
        </span>
      </div>

      <motion.div
        className={`mt-3 max-w-[clamp(140px,24vw,290px)] border-b-2 pb-2 ${
          isLeft ? "border-cyan-300/70" : "border-orange-300/70"
        }`}
        initial={{ opacity: 0, y: 12 }}
        animate={playing ? { opacity: 1, y: 0 } : undefined}
        transition={{ delay: reducedMotion ? 0.12 : 1.18, duration: reducedMotion ? 0.2 : 0.35 }}
      >
        <p className={`truncate text-[clamp(9px,1.1vw,13px)] font-bold tracking-widest ${isLeft ? "text-cyan-200" : "text-orange-200"}`}>
          {playerName || (isLeft ? "我方" : "对手")}
        </p>
        <p className="truncate text-[clamp(14px,2vw,24px)] font-black italic text-white drop-shadow-lg">
          {leaderName}
        </p>
        <p className="text-[clamp(9px,1vw,12px)] font-bold tracking-wider text-white/55">{leaderNumber}</p>
      </motion.div>

      <motion.blockquote
        className={`relative mt-3 w-[clamp(150px,26vw,320px)] rounded-xl border px-3 py-2 text-[clamp(10px,1.35vw,15px)] font-bold leading-relaxed text-white shadow-xl backdrop-blur-md ${
          isLeft
            ? "border-cyan-200/35 bg-cyan-950/75 shadow-cyan-950/50"
            : "border-orange-200/35 bg-orange-950/75 shadow-orange-950/50"
        }`}
        initial={{ opacity: 0, y: 10, scale: 0.96 }}
        animate={playing ? { opacity: 1, y: 0, scale: 1 } : undefined}
        transition={{
          delay: reducedMotion ? (isLeft ? 0.18 : 0.55) : (isLeft ? 1.5 : 2.35),
          duration: reducedMotion ? 0.16 : 0.32,
          ease: "easeOut",
        }}
      >
        <span className={isLeft ? "text-cyan-200" : "text-orange-200"} aria-hidden="true">“</span>
        {quote}
        <span className={isLeft ? "text-cyan-200" : "text-orange-200"} aria-hidden="true">”</span>
      </motion.blockquote>
    </motion.article>
  );
}

/** 街机格斗风格的开局 Leader 对决动画；完全退场后才放行骰点界面。 */
export default function LeaderClashOverlay({ ready, onComplete }: Props) {
  const myLeaderId = useGameStore((state) => state.my?.leaderId ?? "");
  const myLeaderNumber = useGameStore((state) => state.my?.leaderNumber ?? "");
  const myName = useGameStore((state) => state.my?.name ?? "");
  const opponentLeaderId = useGameStore((state) => state.opponent?.leaderId ?? "");
  const opponentLeaderNumber = useGameStore((state) => state.opponent?.leaderNumber ?? "");
  const opponentName = useGameStore((state) => state.opponent?.name ?? "");
  const firstPlayerChosen = useGameStore((state) => state.firstPlayerChosen);
  const turnCount = useGameStore((state) => state.turnCount);
  const reducedMotion = useReducedMotion() ?? false;
  const [phase, setPhase] = useState<IntroPhase>("waiting");
  const completedRef = useRef(false);

  const myLeader = getCard(myLeaderNumber);
  const opponentLeader = getCard(opponentLeaderNumber);
  const mySprite = myLeader?.sprite || CARD_BACK_SRC;
  const opponentSprite = opponentLeader?.sprite || CARD_BACK_SRC;
  const myLeaderName = myLeader?.name || myLeaderNumber;
  const opponentLeaderName = opponentLeader?.name || opponentLeaderNumber;
  const myQuote = getLeaderIntroQuote(myLeaderNumber, myLeaderName);
  const opponentQuote = getLeaderIntroQuote(opponentLeaderNumber, opponentLeaderName);

  const introKey = useMemo(() => {
    if (!myLeaderId || !opponentLeaderId) return "";
    const battleIdentity = [myLeaderId, opponentLeaderId].sort().join(":");
    return `grandumi_leader_clash:${battleIdentity}`;
  }, [myLeaderId, opponentLeaderId]);

  useEffect(() => {
    if (phase !== "waiting" || completedRef.current) return;

    const skipIntro = () => {
      completedRef.current = true;
      setPhase("done");
      onComplete();
    };

    // 已进入正式回合时说明这是恢复中的对局，不应重新遮挡玩家。
    if (turnCount > 0) {
      skipIntro();
      return;
    }

    if (!ready || !introKey || !myLeaderNumber || !opponentLeaderNumber) return;

    let isBotMatch = false;
    try {
      isBotMatch = sessionStorage.getItem("isBotMatch") === "1";
      // 真人对局的先后手已经确定时属于恢复流程；单人测试会在创建时预设先后手，仍需播放开场。
      if (firstPlayerChosen && !isBotMatch) {
        skipIntro();
        return;
      }
      if (sessionStorage.getItem(introKey) === "1") {
        skipIntro();
        return;
      }
    } catch {
      // 浏览器禁用会话存储时仍正常播放，只是不具备刷新去重能力。
      if (firstPlayerChosen) {
        skipIntro();
        return;
      }
    }

    let cancelled = false;
    let started = false;
    const begin = () => {
      if (cancelled || started) return;
      started = true;
      setPhase("playing");
    };

    const preload = (src: string) => new Promise<void>((resolve) => {
      const image = new window.Image();
      image.onload = () => resolve();
      image.onerror = () => resolve();
      image.src = src;
      if (image.complete) resolve();
    });

    void Promise.all([preload(displaySrc(mySprite)), preload(displaySrc(opponentSprite))]).then(begin);
    const fallbackTimer = window.setTimeout(begin, 900);

    return () => {
      cancelled = true;
      window.clearTimeout(fallbackTimer);
    };
  }, [
    firstPlayerChosen,
    introKey,
    myLeaderNumber,
    mySprite,
    onComplete,
    opponentLeaderNumber,
    opponentSprite,
    phase,
    ready,
    turnCount,
  ]);

  useEffect(() => {
    if (phase !== "playing") return;
    const timer = window.setTimeout(() => setPhase("exiting"), reducedMotion ? 2800 : 4800);
    return () => window.clearTimeout(timer);
  }, [phase, reducedMotion]);

  useEffect(() => {
    if (phase !== "exiting") return;
    const timer = window.setTimeout(() => {
      if (completedRef.current) return;
      completedRef.current = true;
      try {
        if (introKey) sessionStorage.setItem(introKey, "1");
      } catch {
        // 会话存储不可用不影响开局流程。
      }
      setPhase("done");
      onComplete();
    }, reducedMotion ? 140 : 320);
    return () => window.clearTimeout(timer);
  }, [introKey, onComplete, phase, reducedMotion]);

  if (phase === "done") return null;

  const playing = phase === "playing";
  const impactDelay = reducedMotion ? 0.12 : 1.02;

  return (
    <motion.div
      className="fixed inset-0 z-[45] overflow-hidden bg-[#030713] text-white"
      role="dialog"
      aria-label="开场领航对决动画"
      initial={{ opacity: 1 }}
      animate={{ opacity: phase === "exiting" ? 0 : 1 }}
      transition={{ duration: reducedMotion ? 0.14 : 0.3 }}
    >
      <div
        className="absolute inset-0 bg-[radial-gradient(circle_at_center,rgba(255,255,255,0.08),transparent_42%),linear-gradient(120deg,#06263a_0%,#07111f_48%,#2a1208_52%,#160805_100%)]"
        aria-hidden="true"
      />
      <div
        className="absolute inset-0 opacity-20"
        style={{ backgroundImage: "repeating-linear-gradient(105deg, transparent 0 22px, rgba(255,255,255,.16) 23px, transparent 25px 48px)" }}
        aria-hidden="true"
      />
      <div
        className="absolute inset-0 bg-cyan-400/10"
        style={{ clipPath: "polygon(0 0, 58% 0, 46% 100%, 0 100%)" }}
        aria-hidden="true"
      />
      <div
        className="absolute inset-0 bg-orange-500/10"
        style={{ clipPath: "polygon(58% 0, 100% 0, 100% 100%, 46% 100%)" }}
        aria-hidden="true"
      />

      <motion.div
        className="absolute inset-0"
        animate={playing && !reducedMotion
          ? {
              x: [0, 0, 0, -12, 10, -7, 4, 0],
              y: [0, 0, 0, 5, -4, 3, -2, 0],
            }
          : undefined}
        transition={{ duration: 1.55, times: [0, 0.62, 0.67, 0.7, 0.74, 0.8, 0.88, 1] }}
      >
        <motion.div
          className="absolute left-1/2 top-1/2 z-20 h-[22vmin] w-[22vmin] rounded-full border-4 border-white/90 shadow-[0_0_70px_25px_rgba(255,255,255,0.55)]"
          initial={{ x: "-50%", y: "-50%", opacity: 0, scale: 0.15 }}
          animate={playing ? { x: "-50%", y: "-50%", opacity: [0, 1, 0], scale: [0.15, 0.2, 2.5] } : undefined}
          transition={{ delay: impactDelay, duration: reducedMotion ? 0.3 : 0.72, times: [0, 0.12, 1] }}
          aria-hidden="true"
        />
        <motion.div
          className="absolute inset-0 z-20 bg-white"
          initial={{ opacity: 0 }}
          animate={playing ? { opacity: [0, 0.86, 0] } : undefined}
          transition={{ delay: impactDelay, duration: reducedMotion ? 0.18 : 0.3, times: [0, 0.15, 1] }}
          aria-hidden="true"
        />

        {!reducedMotion && IMPACT_SHARDS.map((angle, index) => (
          <motion.span
            key={angle}
            className={`absolute left-1/2 top-1/2 z-20 h-[2px] origin-left ${index % 2 === 0 ? "w-[18vmin] bg-cyan-100" : "w-[14vmin] bg-orange-100"}`}
            style={{ rotate: angle }}
            initial={{ opacity: 0, scaleX: 0, x: 8 }}
            animate={playing ? { opacity: [0, 1, 0], scaleX: [0, 1, 1.4], x: [8, 20, 50] } : undefined}
            transition={{ delay: impactDelay + (index % 3) * 0.018, duration: 0.58, ease: "easeOut" }}
            aria-hidden="true"
          />
        ))}

        <div className="absolute inset-0 flex items-center justify-center gap-[clamp(44px,12vw,190px)] px-5 pb-[clamp(10px,4vh,42px)]">
          <FighterCard
            side="left"
            playerName={myName}
            leaderName={myLeaderName}
            leaderNumber={myLeaderNumber}
            quote={myQuote}
            sprite={mySprite}
            playing={playing}
            reducedMotion={reducedMotion}
          />
          <FighterCard
            side="right"
            playerName={opponentName}
            leaderName={opponentLeaderName}
            leaderNumber={opponentLeaderNumber}
            quote={opponentQuote}
            sprite={opponentSprite}
            playing={playing}
            reducedMotion={reducedMotion}
          />
        </div>

        <motion.div
          className="absolute left-1/2 top-1/2 z-30 text-center"
          initial={{ x: "-50%", y: "-50%", opacity: 0, scale: 2.6, rotate: -14 }}
          animate={playing ? { x: "-50%", y: "-50%", opacity: 1, scale: 1, rotate: -6 } : undefined}
          transition={{ delay: reducedMotion ? 0.18 : 1.08, type: "spring", stiffness: 330, damping: 18 }}
        >
          <p className="bg-gradient-to-b from-white via-yellow-200 to-orange-500 bg-clip-text text-[clamp(54px,10vw,132px)] font-black italic leading-none text-transparent drop-shadow-[0_7px_0_rgba(120,25,0,0.9)]">
            VS
          </p>
          <p className="mt-2 whitespace-nowrap text-[clamp(9px,1.2vw,14px)] font-black tracking-[0.45em] text-white/80">
            领航对决
          </p>
        </motion.div>
      </motion.div>

      <motion.div
        className="absolute left-1/2 top-[clamp(14px,4vh,34px)] z-30 text-center"
        initial={{ x: "-50%", opacity: 0, y: -12 }}
        animate={playing ? { x: "-50%", opacity: 1, y: 0 } : undefined}
        transition={{ delay: reducedMotion ? 0.08 : 0.18, duration: 0.35 }}
      >
        <p className="whitespace-nowrap text-[clamp(10px,1.3vw,15px)] font-black tracking-[0.55em] text-white/65">
          BATTLE START
        </p>
      </motion.div>

      {phase === "waiting" && (
        <div className="absolute inset-0 z-40 flex items-center justify-center">
          <div className="h-8 w-8 animate-spin rounded-full border-2 border-white/25 border-t-white/90" />
          <span className="ml-3 text-sm font-bold tracking-wider text-white/60">正在载入领航对决...</span>
        </div>
      )}

      <div className="pointer-events-none absolute inset-0 z-40 shadow-[inset_0_0_120px_55px_rgba(0,0,0,0.72)]" aria-hidden="true" />
    </motion.div>
  );
}
