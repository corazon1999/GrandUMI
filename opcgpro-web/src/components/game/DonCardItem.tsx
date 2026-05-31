"use client";

import { motion } from "framer-motion";
import { clsx } from "clsx";
import type { DonState } from "@/types/game";

interface Props {
  state: DonState;
  isSelected?: boolean;
  onClick?: () => void;
  disabled?: boolean;
  size?: "sm" | "md" | "lg";
}

const sizeClass = {
  sm: "w-[4.5rem] h-[6.3rem]",
  md: "w-[6rem] h-[8.4rem]",
  lg: "w-[8rem] h-[11.2rem]",
};

const textClass = {
  sm: "text-xs",
  md: "text-sm",
  lg: "text-base",
};

const stateStyle: Record<DonState, string> = {
  deck: "border-sky-500/70 bg-gradient-to-br from-sky-950 via-blue-950 to-slate-950 text-sky-200",
  active: "border-yellow-100 bg-yellow-300 text-black cursor-pointer hover:shadow-lg hover:shadow-yellow-300/40",
  rest: "border-zinc-500 bg-zinc-700 text-zinc-300 opacity-75",
  attached: "border-amber-200 bg-amber-500 text-black",
};

export default function DonCardItem({
  state,
  isSelected = false,
  onClick,
  disabled = false,
  size = "md",
}: Props) {
  return (
    <motion.div
      className={clsx(
        sizeClass[size],
        "relative shrink-0 overflow-hidden rounded-md border-2 shadow-xl shadow-black/35 transition-all",
        stateStyle[state],
        isSelected && "ring-2 ring-white",
        disabled && "pointer-events-none",
      )}
      onClick={disabled ? undefined : onClick}
      whileHover={state === "active" && !disabled ? { scale: 1.03 } : undefined}
      whileTap={state === "active" && !disabled ? { scale: 0.97 } : undefined}
      title={`DON ${state}`}
    >
      <div className="absolute inset-2 rounded border border-current/25" />
      <div className="flex h-full w-full items-center justify-center px-2">
        <span className={clsx("font-black leading-none tracking-normal", textClass[size])}>
          DON
        </span>
      </div>
    </motion.div>
  );
}
