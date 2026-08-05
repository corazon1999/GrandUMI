"use client";

import { useMemo, useState, useRef, useCallback, useEffect } from "react";
import { motion, AnimatePresence } from "framer-motion";
import NextImage from "next/image";
import { getAllCachedCards } from "@/data/CardLoader";
import { useDeckStore, FORMAT_RULES, UNLIMITED_COPY_CARDS } from "@/store/deckStore";
import type { CardData } from "@/types/card";
import CardInfoPanel from "@/components/game/CardInfoPanel";
import CardHoverPreview, { type HoverInfo, RARITY_STYLES } from "./CardHoverPreview";
import { thumbSrc } from "@/lib/sprite";
import { useVirtualList } from "@/hooks/useVirtualList";
import { compareCatalogCards, filterAndSortCards } from "@/lib/cardSearch";

const HOVER_DELAY  = 180;   // 悬停多少毫秒后显示（避免划过时闪烁）
const CARD_WIDTH    = 72;   // w-16(64px) + gap
const CARD_HEIGHT   = 108;  // h-24(96px) + 名称行 + gap

// 悬停后立即预加载原图，利用预览出现前的延迟提前完成网络请求。
// 加载失败时移出缓存集合，后续悬停仍可重试。
const preloadedSprites = new Set<string>();
function preloadSprite(src: string) {
  if (!src || preloadedSprites.has(src)) return;
  preloadedSprites.add(src);
  const image = new window.Image();
  image.onerror = () => preloadedSprites.delete(src);
  image.src = src;
}

// ── 主组件 ────────────────────────────────────────────────────────────────
export default function SearchResultPanel() {
  const { format, leader, searchQuery, filterColors, filterType, filterProperty, filterRarity, filterCost, filterSets, filterShowSub1, gridColumns, addCard, setLeader, getCount, notice, clearNotice } =
    useDeckStore();
  const [modal, setModal]     = useState<CardData | null>(null);   // 右键弹窗
  const [hover, setHover]     = useState<HoverInfo | null>(null);  // 悬停预览
  const hoverTimer            = useRef<ReturnType<typeof setTimeout> | null>(null);
  const noticeTimer           = useRef<ReturnType<typeof setTimeout> | null>(null);

  // error notice 2秒后自动清除
  useEffect(() => {
    if (!notice || notice.type !== "error") return;
    if (noticeTimer.current) clearTimeout(noticeTimer.current);
    noticeTimer.current = setTimeout(() => clearNotice(), 2000);
    return () => { if (noticeTimer.current) clearTimeout(noticeTimer.current); };
  }, [notice, clearNotice]);

  const isLeaderMode = filterType === "Leader";

  const results = useMemo(() => {
    const rule = FORMAT_RULES[format];
    const whitelist = isLeaderMode ? rule.leaderSetWhitelist : rule.mainSetWhitelist;
    return filterAndSortCards(
      getAllCachedCards(),
      {
        searchQuery,
        filterColors,
        filterType,
        filterProperty,
        filterRarity,
        filterCost,
        filterSets,
        filterShowSub1,
      },
      {
        allowedSets: whitelist,
        leaderColor: leader?.color,
        sortComparator: filterSets.length === 1 ? compareCatalogCards : undefined,
      },
    );
  }, [format, leader, searchQuery, filterColors, filterType, filterProperty, filterRarity, filterCost, filterSets, filterShowSub1, isLeaderMode]);

  // 虚拟网格列表
  const {
    containerRef: scrollRef,
    totalHeight,
    visibleItems,
  } = useVirtualList({
    itemCount: results.length,
    itemHeight: CARD_HEIGHT,
    rowWidth: CARD_WIDTH,
    columns: gridColumns,
    gap: 8,
    overscan: 2,
  });

  const handleCardClick = (card: CardData) =>
    isLeaderMode ? setLeader(card) : addCard(card);

  // 延迟显示预览，鼠标快速划过时不触发
  const handleMouseEnter = useCallback((card: CardData, rect: DOMRect, currentSprite: string) => {
    if (hoverTimer.current) clearTimeout(hoverTimer.current);
    preloadSprite(currentSprite);
    hoverTimer.current = setTimeout(() => {
      // 延迟期间可能已点击异画箭头，触发时必须读取最新版本，不能使用移入时的旧闭包值。
      const latestSprite = card.sprite ?? currentSprite;
      preloadSprite(latestSprite);
      setHover({ card, rect, currentSprite: latestSprite });
    }, HOVER_DELAY);
  }, []);

  const handleMouseLeave = useCallback(() => {
    if (hoverTimer.current) clearTimeout(hoverTimer.current);
    setHover(null);
  }, []);

  return (
    <div className="flex flex-col h-full">
      {/* 工具栏 */}
      <div className="flex items-center justify-between px-3 py-2 border-b border-gray-800 shrink-0">
        <p className="text-gray-500 text-xs">
          {isLeaderMode ? "选择领航卡" : "搜索结果"} · 共 {results.length} 张
        </p>
      </div>

      {/* 颜色不符提示 */}
      <AnimatePresence>
        {notice?.type === "error" && (
          <motion.div
            initial={{ opacity: 0, y: -6 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -6 }}
            transition={{ duration: 0.15 }}
            className="px-3 py-1.5 bg-red-900/80 border-b border-red-800 shrink-0"
          >
            <p className="text-red-300 text-xs text-center">{notice.message}</p>
          </motion.div>
        )}
      </AnimatePresence>

      {/* 虚拟滚动卡片网格 */}
      <div
        ref={scrollRef}
        className="flex-1 overflow-y-auto p-3"
      >
        {results.length === 0 ? (
          <div className="flex flex-col items-center justify-center h-48 gap-2">
            <p className="text-gray-600 text-sm">没有找到匹配的卡牌</p>
          </div>
        ) : (
          <div className="relative" style={{ height: totalHeight }}>
            {visibleItems.map(({ index, row, col }) => {
              const card = results[index];
              if (!card) return null;
              return (
                <div
                  key={card.number}
                  className="absolute"
                  style={{
                    top: row * (CARD_HEIGHT + 8),
                    left: col * (CARD_WIDTH + 8),
                    width: CARD_WIDTH,
                    height: CARD_HEIGHT,
                  }}
                >
                  <CardGridItem
                    card={card}
                    deckCount={isLeaderMode ? 0 : getCount(card.number)}
                    isLeaderMode={isLeaderMode}
                    onClick={() => handleCardClick(card)}
                    onRightClick={(e) => { e.preventDefault(); setModal(card); }}
                    onMouseEnter={handleMouseEnter}
                    onMouseLeave={handleMouseLeave}
                    onSpriteChange={(sprite) => {
                      preloadSprite(sprite);
                      card.sprite = sprite;
                      setHover((prev) =>
                        prev?.card.number === card.number
                          ? { ...prev, currentSprite: sprite }
                          : prev,
                      );
                    }}
                  />
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* 悬停大图预览（fixed 定位，不受 overflow 裁剪） */}
      <AnimatePresence>
        {hover && <CardHoverPreview info={hover} />}
      </AnimatePresence>

      {/* 右键弹窗 */}
      <CardInfoPanel card={modal} onClose={() => setModal(null)} />
    </div>
  );
}

// ── 单张卡片格子 ──────────────────────────────────────────────────────────

interface CardGridItemProps {
  card: CardData;
  deckCount: number;
  isLeaderMode: boolean;
  onClick: () => void;
  onRightClick: (e: React.MouseEvent) => void;
  onMouseEnter: (card: CardData, rect: DOMRect, currentSprite: string) => void;
  onMouseLeave: () => void;
  onSpriteChange: (sprite: string) => void;
}

function CardGridItem({
  card, deckCount, isLeaderMode, onClick, onRightClick, onMouseEnter, onMouseLeave, onSpriteChange,
}: CardGridItemProps) {
  const isFull    = !isLeaderMode && deckCount >= 4 && !UNLIMITED_COPY_CARDS.has(card.number);
  const hasInDeck = deckCount > 0;

  const sprites   = card.sprites?.length ? card.sprites : [card.sprite ?? "/sprites/CardBack.png"];
  const hasAlts   = sprites.length > 1;
  const [spriteIdx, setSpriteIdx] = useState(sprites.length - 1);
  const [imgFailed, setImgFailed] = useState(false);
  const rawSrc = sprites[spriteIdx] ?? "/sprites/CardBack.png";
  // 默认用缩略图;加载失败(缺缩略图)回退原图
  const currentSrc = imgFailed ? (card.image ?? rawSrc) : thumbSrc(rawSrc);

  // 初始化时同步默认异画的 sprite 到 card 对象，确保添加卡牌时使用正确的异画
  useEffect(() => {
    const initialSprite = sprites[sprites.length - 1];
    if (initialSprite && card.sprite !== initialSprite) {
      card.sprite = initialSprite;
    }
    // 仅首次运行
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const goPrev = (e: React.MouseEvent) => {
    e.stopPropagation();
    setImgFailed(false);
    const next = (spriteIdx - 1 + sprites.length) % sprites.length;
    setSpriteIdx(next);
    onSpriteChange(sprites[next]);
  };
  const goNext = (e: React.MouseEvent) => {
    e.stopPropagation();
    setImgFailed(false);
    const next = (spriteIdx + 1) % sprites.length;
    setSpriteIdx(next);
    onSpriteChange(sprites[next]);
  };

  return (
    <motion.div
      layout
      initial={{ opacity: 0, scale: 0.9 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.9 }}
      transition={{ duration: 0.15 }}
      className="group flex flex-col items-center gap-1 transform-gpu backface-hidden"
      onMouseEnter={(e) => onMouseEnter(card, e.currentTarget.getBoundingClientRect(), rawSrc)}
      onMouseLeave={onMouseLeave}
    >
      <div
        className={`relative w-16 h-24 rounded-lg overflow-hidden select-none border-2 transition-all
          ${isFull
            ? "border-red-800 opacity-50 cursor-not-allowed"
            : isLeaderMode
              ? "border-yellow-600 hover:border-yellow-400 hover:scale-105 cursor-pointer"
              : hasInDeck
                ? "border-orange-500 hover:scale-105 cursor-pointer"
                : "border-gray-700 hover:border-gray-400 hover:scale-105 cursor-pointer"
          }`}
        onClick={isFull ? undefined : onClick}
        onContextMenu={onRightClick}
      >
        <NextImage
          src={currentSrc}
          alt={card.name}
          fill
          sizes="64px"
          className="object-cover"
          draggable={false}
          onError={() => setImgFailed(true)}
        />

        {/* 异画切换箭头（仅有多版本时显示） */}
        {hasAlts && (
          <>
            <button
              className="absolute left-0 top-0 bottom-0 w-4 flex items-center justify-center
                         bg-black/50 hover:bg-black/75 text-white text-xs z-10
                         opacity-0 group-hover:opacity-100 transition-opacity"
              style={{ fontSize: 14 }}
              onClick={goPrev}
              title="上一版本"
            >‹</button>
            <button
              className="absolute right-0 top-0 bottom-0 w-4 flex items-center justify-center
                         bg-black/50 hover:bg-black/75 text-white text-xs z-10
                         opacity-0 group-hover:opacity-100 transition-opacity"
              style={{ fontSize: 14 }}
              onClick={goNext}
              title="下一版本"
            >›</button>
          </>
        )}

        {/* 异画版本指示点 */}
        {hasAlts && (
          <div className="absolute bottom-0 left-0 right-0 flex justify-center gap-0.5 pb-0.5 z-10">
            {sprites.map((_, i) => (
              <span
                key={i}
                className={`block w-1 h-1 rounded-full ${i === spriteIdx ? "bg-white" : "bg-white/40"}`}
              />
            ))}
          </div>
        )}

        {/* 费用（领袖卡不显示） */}
        {card.type !== "Leader" && (
          <div className="absolute top-0.5 left-0.5 bg-black/70 text-white text-[9px] font-bold px-1 rounded">
            {card.cost}
          </div>
        )}
        {/* 稀有度角标 */}
        {card.rarity && (
          <div className={`absolute top-0.5 right-0.5 text-[8px] font-bold px-1 rounded ${RARITY_STYLES[card.rarity] ?? "bg-gray-700 text-white"}`}>
            {card.rarity}
          </div>
        )}
        {/* 威力 */}
        {card.power > 0 && (
          <div className="absolute bottom-0.5 right-0.5 bg-black/70 text-white text-[9px] font-bold px-1 rounded">
            {(card.power / 1000).toFixed(0)}k
          </div>
        )}
        {/* 角标 */}
        {card.subscript > 0 && (
          <div className="absolute bottom-0.5 left-0.5 bg-black/70 text-yellow-400 text-[8px] font-bold px-1 rounded">
            {card.subscript}
          </div>
        )}
        {/* 数量徽章 */}
        {hasInDeck && !isLeaderMode && (
          <div className="absolute top-0.5 right-0.5 w-4 h-4 bg-orange-500 rounded-full flex items-center justify-center">
            <span className="text-white text-[9px] font-bold leading-none">{deckCount}</span>
          </div>
        )}
        {/* 已满遮罩 */}
        {isFull && (
          <div className="absolute inset-0 flex items-center justify-center">
            <span className="text-red-400 text-xs font-bold bg-black/60 px-1 rounded">已满</span>
          </div>
        )}
      </div>

      <p className="text-gray-400 text-[10px] text-center truncate w-full leading-tight px-0.5">
        {card.name}
      </p>
    </motion.div>
  );
}
