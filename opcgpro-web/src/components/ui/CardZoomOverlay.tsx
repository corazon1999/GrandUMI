"use client";

import { useState, useEffect } from "react";
import { motion } from "framer-motion";
import NextImage from "next/image";
import type { CardData } from "@/types/card";
import { toDisplayColor, primaryDisplayColor, COLOR_STYLES } from "@/lib/colorMap";
import { RARITY_STYLES } from "@/components/deck-editor/CardHoverPreview";

const TYPE_LABELS: Record<string, string> = {
  Leader: "领航", Character: "角色", Stage: "舞台", Event: "事件",
};

/**
 * 卡牌大图详情弹窗：对局/手牌等处右键卡牌后居中放大显示。
 * 全屏遮罩 + 居中大卡图 + 结构化信息条；点击遮罩或按 Esc 关闭。
 * portal 由调用方负责（CardItem 已统一 portal 到 body）。
 */
export default function CardZoomOverlay({
  card,
  sprite,
  counterValue,
  onClose,
}: {
  card: CardData;
  sprite: string;
  counterValue?: number;
  onClose: () => void;
}) {
  const rawSprite = sprite ?? card.sprite ?? "/sprites/CardBack.png";
  const displayCounter = counterValue ?? card.counter;
  const [imgSrc, setImgSrc] = useState(rawSprite);

  useEffect(() => {
    setImgSrc(rawSprite);
  }, [rawSprite]);

  // Esc 关闭
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  const displayColor = toDisplayColor(card.color);
  const primary      = primaryDisplayColor(card.color);
  const colorStyle   = COLOR_STYLES[primary];

  return (
    <motion.div
      className="fixed inset-0 z-[120] flex items-center justify-center bg-black/75 backdrop-blur-sm"
      onClick={onClose}
      onContextMenu={(e) => { e.preventDefault(); onClose(); }}
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: 0.15 }}
    >
      <motion.div
        className="flex flex-col items-center gap-3"
        // 内容区也响应点击关闭：右键看完即点任意处退出，无需精确点遮罩
        initial={{ scale: 0.9, opacity: 0 }}
        animate={{ scale: 1, opacity: 1 }}
        exit={{ scale: 0.9, opacity: 0 }}
        transition={{ type: "spring", stiffness: 300, damping: 26 }}
      >
        {/* 大卡图：高度跟随视口，宽按 0.717 卡牌比例；#162 同时按 88vw 约束高度，防竖屏窄屏派生宽度超屏横向溢出 */}
        <div
          className="relative overflow-hidden rounded-2xl border border-gray-600 shadow-2xl"
          style={{ height: "min(78vh, 640px, calc(88vw / 0.717))", aspectRatio: "0.717" }}
        >
          <NextImage
            src={imgSrc}
            alt={card.name}
            fill
            sizes="480px"
            className="object-cover"
            priority
            onError={() =>
              setImgSrc((prev) =>
                card.image && prev !== card.image ? card.image : "/sprites/CardBack.png",
              )
            }
          />
        </div>

        {/* 信息条 */}
        <div className="max-w-[92vw] rounded-xl bg-gray-900/95 px-4 py-3 shadow-xl ring-1 ring-white/10">
          <div className="flex items-center gap-2 flex-wrap">
            <p className="text-white font-bold text-base leading-tight">{card.name}</p>
            {colorStyle && (
              // #179 信息条字号响应式上调一档：手机可读
              <span className={`text-xs sm:text-sm font-bold px-1.5 py-0.5 rounded ${colorStyle.bg} text-white`}>
                {displayColor}
              </span>
            )}
            <span className="text-gray-400 text-xs sm:text-sm">
              {TYPE_LABELS[card.type] ?? card.type}
            </span>
            {card.property && (
              <span className="text-gray-400 text-xs sm:text-sm">{card.property}</span>
            )}
            {card.rarity && (
              <span className={`text-xs sm:text-sm font-bold px-1 rounded ${RARITY_STYLES[card.rarity] ?? "bg-gray-700 text-white"}`}>
                {card.rarity}
              </span>
            )}
            <span className="text-gray-500 text-xs sm:text-sm ml-auto">{card.number}</span>
          </div>

          {/* #179 费/力/反信息行字号响应式：手机端更大 */}
          <div className="mt-1.5 flex items-center gap-4 text-sm sm:text-base">
            {card.cost > 0 && (
              <span className="text-gray-300">费 <span className="text-white font-bold">{card.cost}</span></span>
            )}
            {(card.type === "Character" || card.type === "Leader") && (
              <span className="text-gray-300">力 <span className="text-white font-bold">{card.power.toLocaleString()}</span></span>
            )}
            {displayCounter > 0 && (
              <span className="text-gray-300">反 <span className="text-white font-bold">+{displayCounter}</span></span>
            )}
          </div>

          {card.keyWords.length > 0 && (
            <div className="mt-1.5 flex flex-wrap gap-1">
              {card.keyWords.map((k) => (
                <span key={k} className="text-xs sm:text-sm bg-blue-900/60 text-blue-300 px-1.5 py-0.5 rounded">
                  {k}
                </span>
              ))}
            </div>
          )}

          {card.abilities.length > 0 && (
            <div className="mt-1.5 flex flex-wrap gap-1">
              {card.abilities.map((a) => (
                <span key={a} className="text-xs sm:text-sm bg-emerald-900/60 text-emerald-300 px-1.5 py-0.5 rounded">
                  {a}
                </span>
              ))}
            </div>
          )}

          {card.trigger && (
            // #179 触发文本字号响应式：手机端从 12px 提升到 sm/base
            <p className="mt-2 text-sm sm:text-base leading-snug text-amber-200">
              <span className="font-bold">触发</span> {card.trigger}
            </p>
          )}
        </div>
      </motion.div>
    </motion.div>
  );
}
