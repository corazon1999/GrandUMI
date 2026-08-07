"use client";

import { useState, useEffect } from "react";
import { motion } from "framer-motion";
import NextImage from "next/image";
import type { CardData } from "@/types/card";
import { toDisplayColor, primaryDisplayColor, COLOR_STYLES } from "@/lib/colorMap";
import { CARD_BACK_SRC, displaySrc, nextCardImageSrc, thumbSrc } from "@/lib/sprite";

export const PREVIEW_W = 240;
const PREVIEW_H_APPROX = 480;

export interface HoverInfo {
  card: CardData;
  rect: DOMRect;
  currentSprite: string;
}

export const RARITY_STYLES: Record<string, string> = {
  L:   "bg-yellow-500 text-black",
  SR:  "bg-pink-500 text-white",
  R:   "bg-sky-500 text-white",
  UC:  "bg-gray-500 text-white",
  U:   "bg-gray-500 text-white",
  C:   "bg-gray-700 text-gray-300",
  SEC: "bg-red-600 text-white",
  P:   "bg-emerald-500 text-white",
};

const TYPE_LABELS: Record<string, string> = {
  Leader: "领航", Character: "角色", Stage: "舞台", Event: "事件",
};

export default function CardHoverPreview({
  info,
  counterValue,
}: {
  info: HoverInfo;
  counterValue?: number;
}) {
  const { card, rect, currentSprite } = info;
  const displayCounter = counterValue ?? card.counter;
  const rawSprite = currentSprite ?? card.sprite ?? CARD_BACK_SRC;
  // 先显示 128px 小图，再由高质量 WebP 平滑覆盖；派生图缺失时才回退原图。
  const [imgSrc, setImgSrc] = useState(displaySrc(rawSprite));
  const [loadedImageSrc, setLoadedImageSrc] = useState<string | null>(null);

  useEffect(() => {
    setImgSrc(displaySrc(rawSprite));
    setLoadedImageSrc(null);
  }, [rawSprite]);

  const spaceRight = window.innerWidth - rect.right;
  const showRight  = spaceRight >= PREVIEW_W + 16;
  const x = showRight
    ? rect.right + 12
    : rect.left - 12 - PREVIEW_W;

  const cardCenterY = rect.top + rect.height / 2;
  const rawY        = cardCenterY - PREVIEW_H_APPROX / 2;
  const y           = Math.max(8, Math.min(rawY, window.innerHeight - PREVIEW_H_APPROX - 8));

  const displayColor = toDisplayColor(card.color);
  const primary      = primaryDisplayColor(card.color);
  const colorStyle   = COLOR_STYLES[primary];

  return (
    <motion.div
      className="fixed z-50 rounded-xl overflow-hidden shadow-2xl border border-gray-700"
      style={{ width: PREVIEW_W, left: x, top: y, pointerEvents: "none" }}
      initial={{ opacity: 0, scale: 0.93, x: showRight ? -8 : 8 }}
      animate={{ opacity: 1, scale: 1,   x: 0 }}
      exit={{    opacity: 0, scale: 0.93, x: showRight ? -8 : 8 }}
      transition={{ duration: 0.15, ease: "easeOut" }}
    >
      <div className="relative bg-gray-900" style={{ height: PREVIEW_W * 1.4 }}>
        {/* 网格缩略图仅约 6KB，先即时显示；原图就绪后再平滑覆盖。 */}
        <NextImage
          src={thumbSrc(rawSprite)}
          alt=""
          fill
          sizes="480px"
          className="object-cover"
          unoptimized
        />
        <NextImage
          key={imgSrc}
          src={imgSrc}
          alt={card.name}
          fill
          sizes="480px"
          className={`object-cover transition-opacity duration-150 ${loadedImageSrc === imgSrc ? "opacity-100" : "opacity-0"}`}
          unoptimized
          onLoad={() => setLoadedImageSrc(imgSrc)}
          onError={() => {
            setLoadedImageSrc(null);
            setImgSrc((prev) => nextCardImageSrc(prev, rawSprite, card.image, "display"));
          }}
        />
        {colorStyle && (
          <div className={`absolute bottom-0 left-0 right-0 h-1.5 ${colorStyle.bg}`} />
        )}
      </div>

      <div className="bg-gray-900 p-2.5 flex flex-col gap-1.5">
        <p className="text-white font-bold text-sm leading-tight">{card.name}</p>

        <div className="flex items-center gap-1.5 flex-wrap">
          {colorStyle && (
            <span className={`text-[10px] font-bold px-1.5 py-0.5 rounded ${colorStyle.bg} text-white`}>
              {displayColor}
            </span>
          )}
          <span className="text-gray-400 text-[10px]">
            {TYPE_LABELS[card.type] ?? card.type}
          </span>
          {card.rarity && (
            <span className={`text-[9px] font-bold px-1 rounded ${RARITY_STYLES[card.rarity] ?? "bg-gray-700 text-white"}`}>
              {card.rarity}
            </span>
          )}
          {card.subscript > 0 && (
            <span className="text-yellow-400 text-[9px] font-bold">角标{card.subscript}</span>
          )}
          <span className="text-gray-600 text-[10px] ml-auto">{card.number}</span>
        </div>

        <div className="flex items-center gap-3 text-[11px]">
          {card.cost > 0 && (
            <span className="text-gray-300">
              费 <span className="text-white font-bold">{card.cost}</span>
            </span>
          )}
          {card.power > 0 && (
            <span className="text-gray-300">
              力 <span className="text-white font-bold">{card.power.toLocaleString()}</span>
            </span>
          )}
          {displayCounter > 0 && (
            <span className="text-gray-300">
              反 <span className="text-white font-bold">+{displayCounter}</span>
            </span>
          )}
        </div>

        {card.keyWords.length > 0 && (
          <div className="flex flex-wrap gap-1">
            {card.keyWords.map((k) => (
              <span key={k} className="text-[9px] bg-blue-900/60 text-blue-300 px-1.5 py-0.5 rounded">
                {k}
              </span>
            ))}
          </div>
        )}
      </div>
    </motion.div>
  );
}
