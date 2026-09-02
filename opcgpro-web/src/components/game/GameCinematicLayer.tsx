"use client";

import { useEffect, useMemo, useState } from "react";
import { useLanguage } from "@/i18n/LanguageProvider";
import { nextGameCinematicDeadline } from "@/lib/gameCinematic.mjs";
import { useGameStore } from "@/store/gameStore";

function useReducedMotionPreference(): boolean {
  const [reduced, setReduced] = useState(false);
  useEffect(() => {
    const query = window.matchMedia("(prefers-reduced-motion: reduce)");
    const update = () => setReduced(query.matches);
    update();
    query.addEventListener("change", update);
    return () => query.removeEventListener("change", update);
  }, []);
  return reduced;
}

/** 只调度本地展示阶段；胜负、结算和房间清理由服务端独立完成。 */
export function GameCinematicController() {
  const reducedMotion = useReducedMotionPreference();
  const phase = useGameStore((state) => state.cinematic.phase);
  const phaseStartedAt = useGameStore((state) => state.cinematic.phaseStartedAt);
  const terminalEventId = useGameStore((state) => state.cinematic.terminalEventId);
  const openingDeadlines = useGameStore((state) =>
    state.cinematic.openingBubbles.map((bubble) => `${bubble.eventId}:${bubble.expiresAt}`).join("|"),
  );

  useEffect(() => {
    let timer: number | null = null;
    const advance = () => {
      const now = Date.now();
      useGameStore.getState().advanceCinematic(now, reducedMotion);
      const deadline = nextGameCinematicDeadline(
        useGameStore.getState().cinematic,
        reducedMotion,
      );
      if (deadline !== null) {
        timer = window.setTimeout(advance, Math.max(0, deadline - Date.now()));
      }
    };
    advance();
    return () => {
      if (timer !== null) window.clearTimeout(timer);
    };
  }, [openingDeadlines, phase, phaseStartedAt, reducedMotion, terminalEventId]);

  return null;
}

function cinematicBubbleClass(styleToken: string): string {
  if (["sunset", "feast"].includes(styleToken))
    return "border-orange-300/80 from-orange-950/95 via-rose-950/95 to-slate-950/95 text-orange-50";
  if (["tide", "shock", "navy"].includes(styleToken))
    return "border-cyan-300/80 from-cyan-950/95 via-blue-950/95 to-slate-950/95 text-cyan-50";
  if (["gold", "wanted"].includes(styleToken))
    return "border-amber-200/85 from-amber-950/95 via-yellow-950/95 to-stone-950/95 text-amber-50";
  if (["haki", "emperor"].includes(styleToken))
    return "border-fuchsia-300/85 from-fuchsia-950/95 via-purple-950/95 to-black/95 text-fuchsia-50";
  if (styleToken === "leaf")
    return "border-emerald-300/80 from-emerald-950/95 via-teal-950/95 to-slate-950/95 text-emerald-50";
  if (styleToken === "ember")
    return "border-red-300/85 from-red-950/95 via-orange-950/95 to-slate-950/95 text-red-50";
  return "border-slate-300/70 from-slate-700/95 via-slate-900/95 to-gray-950/95 text-slate-50";
}

export function LeaderCinematicAnchor({ side }: { side: "self" | "opponent" }) {
  const { t } = useLanguage();
  const cinematic = useGameStore((state) => state.cinematic);
  const opening = useMemo(
    () => cinematic.openingBubbles.find((bubble) => bubble.displaySide === side) ?? null,
    [cinematic.openingBubbles, side],
  );
  const victory = cinematic.phase === "victory"
    && cinematic.terminal?.victory?.displaySide === side
      ? cinematic.terminal.victory
      : null;
  const bubble = victory ?? (cinematic.phase === "idle" ? opening : null);
  const defeated = cinematic.phase === "impact" && cinematic.terminal?.loserSide === side;

  return (
    <div
      data-game-cinematic-leader-anchor={side}
      className="pointer-events-none absolute inset-0 z-[70]"
      aria-live="polite"
    >
      {defeated && (
        <div data-leader-defeat-impact className="leader-defeat-impact absolute -inset-5" aria-hidden="true">
          <span className="leader-defeat-flash" />
          <span className="leader-defeat-shockwave" />
          <span className="leader-defeat-fracture leader-defeat-fracture--one" />
          <span className="leader-defeat-fracture leader-defeat-fracture--two" />
          <span className="leader-defeat-fracture leader-defeat-fracture--three" />
          <span className="leader-defeat-shard leader-defeat-shard--one" />
          <span className="leader-defeat-shard leader-defeat-shard--two" />
          <span className="leader-defeat-shard leader-defeat-shard--three" />
          <span className="leader-defeat-shard leader-defeat-shard--four" />
        </div>
      )}
      {bubble && (
        <div
          key={bubble.eventId}
          data-game-cinematic-bubble={victory ? "victory" : "opening"}
          data-display-side={side}
          data-style-token={bubble.styleToken}
          role="status"
          className={`game-cinematic-bubble absolute right-[calc(100%+0.875rem)] top-1/2 w-[20rem] -translate-y-1/2 rounded-2xl border bg-gradient-to-br px-4 py-3 text-sm font-bold leading-relaxed shadow-2xl ring-1 ring-white/15 ${victory ? "game-cinematic-bubble--victory" : "game-cinematic-bubble--opening"} ${cinematicBubbleClass(bubble.styleToken)}`}
        >
          <span className="mb-1 block truncate text-[10px] font-black uppercase tracking-[0.16em] opacity-70">
            {victory ? t("胜利宣言") : bubble.displayName}
          </span>
          <span className="line-clamp-4 [overflow-wrap:anywhere]">{t(bubble.text)}</span>
          <span aria-hidden="true" className="absolute -right-2 top-1/2 h-4 w-4 -translate-y-1/2 rotate-45 border-r border-t border-current bg-inherit opacity-90" />
        </div>
      )}
    </div>
  );
}
