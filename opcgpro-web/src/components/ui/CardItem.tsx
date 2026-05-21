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
  sm: "w-14 h-20",
  md: "w-20 h-28",
  lg: "w-28 h-40",
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
        "relative rounded-lg overflow-hidden cursor-pointer shrink-0",
        "border-2 transition-colors",
        "transform-gpu backface-hidden",
        isSelected
          ? "border-yellow-400 shadow-lg shadow-yellow-400/40"
          : "border-gray-700 hover:border-gray-500"
      )}
      animate={{
        rotate: isTapped ? 90 : 0,
        y: isSelected ? -10 : 0,
        scale: isSelected ? 1.05 : 1,
      }}
      transition={{ type: "spring", stiffness: 300, damping: 25 }}
      onClick={onClick}
      whileHover={!isSelected ? { scale: 1.03 } : {}}
    >
      {showFaceDown ? (
        <div className="w-full h-full bg-gradient-to-br from-blue-900 to-blue-950 flex items-center justify-center">
          <span className="text-blue-400 text-xs font-bold">CARD</span>
        </div>
      ) : (
        <>
          <NextImage
            src={imgSrc}
            alt={card!.name}
            fill
            sizes="160px"
            className="object-cover"
            draggable={false}
            onError={() => setImgSrc("/sprites/CardBack.png")}
          />
          <div className="absolute bottom-0 left-0 right-0 flex justify-between px-1 pb-0.5 text-xs font-bold">
            <span className="bg-black/70 text-white px-1 rounded text-[10px]">
              {card!.cost}
            </span>
            <div className="flex items-center gap-0.5">
              {/* 咚标记 */}
              {attachedDonCount > 0 && (
                <span className="bg-yellow-500/80 text-black px-0.5 rounded text-[9px] font-bold leading-tight">
                  咚×{attachedDonCount}
                </span>
              )}
              {card!.power > 0 && (
                <span
                  className={clsx(
                    "bg-black/70 px-1 rounded text-[10px]",
                    powerBuff + attachedDonCount * 1000 > 0
                      ? "text-green-400"
                      : powerBuff < 0
                        ? "text-red-400"
                        : "text-white"
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
