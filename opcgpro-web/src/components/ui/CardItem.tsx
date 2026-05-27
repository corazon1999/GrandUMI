"use client";

import { motion } from "framer-motion";
import NextImage from "next/image";
import { useState } from "react";
import type { CardData } from "@/types/card";
import { clsx } from "clsx";

interface Props {
  card: CardData | null;
  isSelected?: boolean;
  isTapped?: boolean;
  powerBuff?: number;
  attachedDonCount?: number;
  faceDown?: boolean;
  onClick?: () => void;
  size?: "sm" | "md" | "lg";
}

const sizes = {
  sm: "w-[4.5rem] h-[6.3rem]",
  md: "w-[6rem] h-[8.4rem]",
  lg: "w-[8rem] h-[11.2rem]",
};

export default function CardItem({
  card,
  isSelected = false,
  isTapped = false,
  powerBuff = 0,
  attachedDonCount = 0,
  faceDown = false,
  onClick,
  size = "md",
}: Props) {
  const showFaceDown = faceDown || !card;
  const donPower = attachedDonCount * 1000;
  const displayPower = (card?.power ?? 0) + powerBuff + donPower;
  const [imgSrc, setImgSrc] = useState(card?.sprite ?? "/sprites/CardBack.png");

  return (
    <motion.div
      className={clsx(
        sizes[size],
        "relative shrink-0 cursor-pointer overflow-hidden rounded-md border-2 shadow-xl shadow-black/35",
        "transform-gpu backface-hidden transition-colors",
        isSelected
          ? "border-yellow-300 shadow-yellow-300/40"
          : "border-slate-500/70 hover:border-slate-200",
      )}
      animate={{
        rotate: isTapped ? 90 : 0,
        y: isSelected ? -12 : 0,
        scale: isSelected ? 1.05 : 1,
      }}
      transition={{ type: "spring", stiffness: 300, damping: 25 }}
      onClick={onClick}
      whileHover={!isSelected ? { scale: 1.03 } : {}}
    >
      {showFaceDown ? (
        <div className="flex h-full w-full items-center justify-center bg-gradient-to-br from-sky-950 via-blue-950 to-slate-950 ring-1 ring-inset ring-sky-300/20">
          <span className="text-xs font-black tracking-normal text-sky-300">CARD</span>
        </div>
      ) : (
        <>
          <NextImage
            src={imgSrc}
            alt={card!.name}
            fill
            sizes="180px"
            className="object-cover"
            draggable={false}
            onError={() => setImgSrc("/sprites/CardBack.png")}
          />
          <div className="absolute inset-x-0 bottom-0 flex justify-between gap-1 bg-gradient-to-t from-black/90 via-black/50 to-transparent px-1.5 pb-1 pt-6 text-xs font-bold">
            <span className="rounded bg-black/85 px-1.5 text-[11px] text-white ring-1 ring-white/15">
              {card!.cost}
            </span>
            <div className="flex items-center gap-1">
              {attachedDonCount > 0 && (
                <span className="rounded bg-yellow-300 px-1 text-[10px] font-black leading-tight text-black">
                  DONx{attachedDonCount}
                </span>
              )}
              {card!.power > 0 && (
                <span
                  className={clsx(
                    "rounded bg-black/85 px-1.5 text-[11px] ring-1 ring-white/15",
                    powerBuff + attachedDonCount * 1000 > 0
                      ? "text-green-300"
                      : powerBuff < 0
                        ? "text-red-300"
                        : "text-white",
                  )}
                >
                  {displayPower.toLocaleString()}
                </span>
              )}
            </div>
          </div>
        </>
      )}
    </motion.div>
  );
}
