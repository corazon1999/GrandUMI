"use client";

import { AnimatePresence, motion } from "framer-motion";
import { useEffect, useMemo, useRef, useState } from "react";
import { eventBus } from "@/net/eventBus";

const MAX_VISIBLE_SPECTATORS = 10;
const BUBBLE_DURATION_MS = 4000;
const AVATAR_STYLES = [
  "from-fuchsia-500 to-purple-950 ring-fuchsia-300/60",
  "from-sky-400 to-blue-950 ring-sky-200/60",
  "from-emerald-400 to-teal-950 ring-emerald-200/60",
  "from-amber-400 to-orange-950 ring-amber-200/60",
  "from-rose-400 to-red-950 ring-rose-200/60",
  "from-indigo-400 to-violet-950 ring-indigo-200/60",
];

interface SpectatorDetail {
  account: string;
  name: string;
  viewingYou: boolean;
  handVisible: boolean;
}

interface SpectatorSeat extends SpectatorDetail {
  key: string;
}

interface SpectatorBubble {
  id: number;
  text: string;
}

function normalizeIdentity(value?: string) {
  return value?.trim().toLocaleLowerCase() ?? "";
}

function avatarInitial(name: string) {
  return Array.from(name.trim())[0]?.toLocaleUpperCase() || "观";
}

function avatarStyle(identity: string) {
  let hash = 0;
  for (const character of identity) {
    hash = (hash * 31 + character.codePointAt(0)!) >>> 0;
  }
  return AVATAR_STYLES[hash % AVATAR_STYLES.length];
}

export default function SpectatorArena({
  spectatorNames,
  spectatorDetails,
  muted,
  onKick,
  kickConfirm,
}: {
  spectatorNames: string[];
  spectatorDetails: SpectatorDetail[];
  muted: boolean;
  onKick: (account: string) => void;
  kickConfirm: string;
}) {
  const seats = useMemo<SpectatorSeat[]>(() => {
    const source =
      spectatorDetails.length > 0
        ? spectatorDetails
        : spectatorNames.map((name) => ({
            account: "",
            name,
            viewingYou: false,
            handVisible: false,
          }));

    return source.map((spectator, index) => {
      const account = normalizeIdentity(spectator.account);
      return {
        ...spectator,
        key: account
          ? `account:${account}`
          : `name:${normalizeIdentity(spectator.name)}:${index}`,
      };
    });
  }, [spectatorDetails, spectatorNames]);

  const [selectedSeat, setSelectedSeat] = useState("");
  const [bubbles, setBubbles] = useState<Record<string, SpectatorBubble>>({});
  const seatsRef = useRef(seats);
  const mutedRef = useRef(muted);
  const bubbleIdRef = useRef(0);
  const bubbleTimersRef = useRef(new Map<string, ReturnType<typeof setTimeout>>());

  useEffect(() => {
    seatsRef.current = seats;
    if (selectedSeat && !seats.some((seat) => seat.key === selectedSeat)) {
      setSelectedSeat("");
    }
  }, [seats, selectedSeat]);

  useEffect(() => {
    mutedRef.current = muted;
  }, [muted]);

  useEffect(() => {
    const handler = (message: {
      text: string;
      fromAccount?: string;
      fromName: string;
      fromRole: "player" | "spectator";
    }) => {
      if (message.fromRole !== "spectator" || mutedRef.current) return;

      const account = normalizeIdentity(message.fromAccount);
      const name = normalizeIdentity(message.fromName);
      const seat = seatsRef.current.find(
        (candidate) => {
          const candidateAccount = normalizeIdentity(candidate.account);
          return (
            (account && candidateAccount === account) ||
            (!candidateAccount && normalizeIdentity(candidate.name) === name)
          );
        },
      );
      if (!seat) return;

      const id = ++bubbleIdRef.current;
      setBubbles((previous) => ({
        ...previous,
        [seat.key]: { id, text: message.text },
      }));

      const previousTimer = bubbleTimersRef.current.get(seat.key);
      if (previousTimer) clearTimeout(previousTimer);
      const timer = setTimeout(() => {
        setBubbles((previous) => {
          if (previous[seat.key]?.id !== id) return previous;
          const next = { ...previous };
          delete next[seat.key];
          return next;
        });
        bubbleTimersRef.current.delete(seat.key);
      }, BUBBLE_DURATION_MS);
      bubbleTimersRef.current.set(seat.key, timer);
    };

    eventBus.on("gameChat", handler);
    const timers = bubbleTimersRef.current;
    return () => {
      eventBus.off("gameChat", handler);
      for (const timer of timers.values()) clearTimeout(timer);
      timers.clear();
    };
  }, []);

  const visibleSeats = seats.slice(0, MAX_VISIBLE_SPECTATORS);
  const overflowCount = seats.length - visibleSeats.length;

  return (
    <aside
      data-spectator-arena
      aria-label={`${seats.length} 人正在观战`}
      className="pointer-events-none fixed z-[45] hidden md:flex md:flex-col-reverse md:items-start md:gap-2"
      style={{
        left: "calc(0.75rem + var(--layout-safe-left, env(safe-area-inset-left)))",
        top: "calc(0.75rem + var(--layout-safe-top, env(safe-area-inset-top)))",
        bottom:
          "calc(4.75rem + var(--layout-safe-bottom, env(safe-area-inset-bottom)))",
      }}
    >
      {visibleSeats.map((spectator, index) => {
        const bubble = bubbles[spectator.key];
        const selected = selectedSeat === spectator.key;
        return (
          <div
            key={spectator.key}
            data-spectator-seat
            data-seat-order={index + 1}
            className="relative shrink-0"
          >
            <button
              type="button"
              onClick={() =>
                setSelectedSeat((current) =>
                  current === spectator.key ? "" : spectator.key,
                )
              }
              className={`pointer-events-auto relative flex h-12 w-12 items-center justify-center rounded-full bg-gradient-to-br text-sm font-black text-white shadow-[0_6px_18px_rgba(0,0,0,0.45)] ring-2 transition hover:scale-105 hover:brightness-110 ${avatarStyle(spectator.account || spectator.name)}`}
              title={spectator.name}
              aria-label={`观战者 ${spectator.name}`}
              aria-expanded={selected}
            >
              <span className="absolute inset-1 rounded-full border border-white/25 bg-slate-950/20" />
              <span className="relative drop-shadow">{avatarInitial(spectator.name)}</span>
              <span className="absolute -bottom-0.5 -right-0.5 h-3 w-3 rounded-full border-2 border-[#07111f] bg-emerald-400 shadow-[0_0_8px_rgba(52,211,153,0.9)]" />
            </button>

            <AnimatePresence initial={false}>
              {bubble ? (
                <motion.div
                  key={`bubble:${bubble.id}`}
                  data-spectator-chat-bubble
                  className="pointer-events-none absolute bottom-1/2 left-[calc(100%+0.75rem)] z-20 w-max max-w-[min(19rem,calc(100vw-6rem))] translate-y-1/2 rounded-2xl rounded-bl-sm border border-purple-200/25 bg-slate-950/95 px-3 py-2 text-left shadow-[0_10px_32px_rgba(0,0,0,0.5)]"
                  initial={{ opacity: 0, x: -10, scale: 0.92 }}
                  animate={{ opacity: 1, x: 0, scale: 1 }}
                  exit={{ opacity: 0, x: -6, scale: 0.96 }}
                  transition={{ duration: 0.18, ease: "easeOut" }}
                >
                  <span className="block text-[10px] font-bold text-purple-300">
                    {spectator.name}
                  </span>
                  <span className="block break-words text-xs leading-5 text-white">
                    {bubble.text}
                  </span>
                </motion.div>
              ) : selected ? (
                <motion.div
                  key="details"
                  data-spectator-seat-details
                  className="pointer-events-auto absolute bottom-1/2 left-[calc(100%+0.75rem)] z-10 w-48 translate-y-1/2 rounded-xl border border-purple-300/25 bg-slate-950/95 p-3 text-left text-xs text-white shadow-2xl"
                  initial={{ opacity: 0, x: -8, scale: 0.95 }}
                  animate={{ opacity: 1, x: 0, scale: 1 }}
                  exit={{ opacity: 0, x: -5, scale: 0.97 }}
                  transition={{ duration: 0.15 }}
                >
                  <p className="truncate font-bold text-purple-200">
                    {spectator.name}
                  </p>
                  {spectator.viewingYou && (
                    <p className="mt-1 text-[10px] text-purple-300">
                      主视角：你{spectator.handVisible ? " · 已公开手牌" : ""}
                    </p>
                  )}
                  {spectatorDetails.length > 0 && (
                    <button
                      type="button"
                      onClick={() => onKick(spectator.account)}
                      className={`mt-2 min-h-12 w-full rounded-lg px-3 text-[11px] font-bold ${kickConfirm === spectator.account ? "bg-red-800 text-white" : "bg-slate-800 text-slate-200 hover:bg-red-900 hover:text-red-100"}`}
                    >
                      {kickConfirm === spectator.account
                        ? "确认移出"
                        : "移出观战席"}
                    </button>
                  )}
                </motion.div>
              ) : null}
            </AnimatePresence>
          </div>
        );
      })}

      {overflowCount > 0 && (
        <div
          data-spectator-overflow
          className="flex h-12 w-12 shrink-0 items-center justify-center rounded-full border border-dashed border-purple-300/50 bg-purple-950/80 text-xs font-black text-purple-100 shadow-lg"
          title={`另有 ${overflowCount} 人正在观战`}
        >
          +{overflowCount > 99 ? "99" : overflowCount}
        </div>
      )}
    </aside>
  );
}
