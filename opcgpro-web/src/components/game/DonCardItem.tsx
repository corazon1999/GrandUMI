"use client";

import { motion } from "framer-motion";
import { clsx } from "clsx";
import type { DonState } from "@/types/game";

interface Props {
  state: DonState;
  isSelected?: boolean;
  onClick?: () => void;
  disabled?: boolean;
  size?: "sm" | "lg";
}

const sizeClass = {
  sm: "w-4 h-6",
  lg: "w-6 h-9",
};

const base = "rounded-sm border transition-all shrink-0";

const stateStyle: Record<DonState, string> = {
  deck: "bg-blue-900 border-blue-700 opacity-60",
  active: "bg-yellow-500 border-yellow-400 cursor-pointer hover:scale-110 hover:shadow-lg hover:shadow-yellow-400/30",
  rest: "bg-gray-700 border-gray-600 rotate-90 opacity-50",
  attached: "bg-yellow-600 border-yellow-500",
};

export default function DonCardItem({
  state,
  isSelected = false,
  onClick,
  disabled = false,
  size = "sm",
}: Props) {
  const label: Record<DonState, string> = {
    deck: "D",
    active: "咚",
    rest: "咚",
    attached: "咚",
  };

  return (
    <motion.div
      className={clsx(
        base,
        sizeClass[size],
        stateStyle[state],
        isSelected && "ring-2 ring-white scale-110",
        disabled && "pointer-events-none",
      )}
      onClick={disabled ? undefined : onClick}
      whileHover={state === "active" && !disabled ? { scale: 1.15 } : undefined}
      whileTap={state === "active" && !disabled ? { scale: 0.95 } : undefined}
    >
      <div className="w-full h-full flex items-center justify-center">
        <span
          className={clsx(
            "font-bold leading-none",
            size === "sm" ? "text-[7px]" : "text-[9px]",
            state === "active" ? "text-black" : "text-gray-400",
            state === "deck" && "text-blue-400",
          )}
        >
          {label[state]}
        </span>
      </div>
    </motion.div>
  );
}
