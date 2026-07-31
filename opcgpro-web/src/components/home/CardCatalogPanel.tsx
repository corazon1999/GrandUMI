"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import type { CardData } from "@/types/card";
import { getAllCachedCards, loadAllCards } from "@/data/CardLoader";
import { ALL_SET_NAMES } from "@/data/cardSets";
import { useVirtualList } from "@/hooks/useVirtualList";
import { thumbSrc } from "@/lib/sprite";
import CardInfoPanel from "@/components/game/CardInfoPanel";

const TYPE_OPTIONS = [
  { value: "Leader", label: "领航" },
  { value: "Character", label: "角色" },
  { value: "Event", label: "事件" },
  { value: "Stage", label: "舞台" },
] as const;

const COLOR_OPTIONS = ["红", "绿", "蓝", "紫", "黑", "黄"];
const RARITY_OPTIONS = ["L", "SEC", "SR", "R", "UC", "C", "P"];
const COST_OPTIONS = Array.from({ length: 11 }, (_, index) => index);

const CARD_WIDTH = 118;
const CARD_HEIGHT = 198;
const CARD_GAP = 12;

type LoadState = "loading" | "done" | "error";

function cardSetOf(card: CardData): string {
  return card.number.split("-")[0];
}

function cardMatchesColor(card: CardData, color: string): boolean {
  return !color || card.color.split("/").includes(color);
}

export default function CardCatalogPanel() {
  const [loadState, setLoadState] = useState<LoadState>("loading");
  const [cards, setCards] = useState<CardData[]>([]);
  const [selectedCard, setSelectedCard] = useState<CardData | null>(null);

  const [query, setQuery] = useState("");
  const [filterSet, setFilterSet] = useState("");
  const [filterColor, setFilterColor] = useState("");
  const [filterType, setFilterType] = useState("");
  const [filterRarity, setFilterRarity] = useState("");
  const [filterCost, setFilterCost] = useState("");

  const loadCards = useCallback(async () => {
    setLoadState("loading");
    try {
      await loadAllCards();
      setCards(
        getAllCachedCards()
          .slice()
          .sort((a, b) => a.number.localeCompare(b.number, undefined, { numeric: true })),
      );
      setLoadState("done");
    } catch {
      setLoadState("error");
    }
  }, []);

  useEffect(() => {
    void loadCards();
  }, [loadCards]);

  const filteredCards = useMemo(() => {
    const normalizedQuery = query.trim().toLocaleLowerCase();
    const cost = filterCost === "" ? null : Number(filterCost);

    return cards.filter((card) => {
      const matchesQuery =
        !normalizedQuery ||
        card.name.toLocaleLowerCase().includes(normalizedQuery) ||
        card.number.toLocaleLowerCase().includes(normalizedQuery);

      return (
        matchesQuery &&
        (!filterSet || cardSetOf(card) === filterSet) &&
        cardMatchesColor(card, filterColor) &&
        (!filterType || card.type === filterType) &&
        (!filterRarity || card.rarity === filterRarity) &&
        (cost === null || card.cost === cost)
      );
    });
  }, [cards, query, filterSet, filterColor, filterType, filterRarity, filterCost]);

  const { containerRef, totalHeight, visibleItems } = useVirtualList({
    itemCount: filteredCards.length,
    itemHeight: CARD_HEIGHT,
    rowWidth: CARD_WIDTH,
    columns: 1,
    gap: CARD_GAP,
    overscan: 3,
  });

  useEffect(() => {
    containerRef.current?.scrollTo({ top: 0 });
  }, [query, filterSet, filterColor, filterType, filterRarity, filterCost, containerRef]);

  const hasFilters = Boolean(
    query || filterSet || filterColor || filterType || filterRarity || filterCost,
  );

  const resetFilters = () => {
    setQuery("");
    setFilterSet("");
    setFilterColor("");
    setFilterType("");
    setFilterRarity("");
    setFilterCost("");
  };

  if (loadState === "loading") {
    return (
      <div className="flex h-full flex-col items-center justify-center gap-3 bg-gray-950">
        <div className="h-8 w-8 animate-spin rounded-full border-2 border-gray-700 border-t-orange-500" />
        <p className="text-sm text-gray-400">正在加载卡牌图鉴…</p>
      </div>
    );
  }

  if (loadState === "error") {
    return (
      <div className="flex h-full flex-col items-center justify-center gap-3 bg-gray-950">
        <p className="text-sm text-red-400">卡牌数据加载失败</p>
        <button
          type="button"
          onClick={() => void loadCards()}
          className="rounded-lg bg-orange-500 px-4 py-2 text-sm font-bold text-white transition-colors hover:bg-orange-400"
        >
          重新加载
        </button>
      </div>
    );
  }

  return (
    <section className="flex h-full min-w-0 flex-col bg-gray-950">
      <header className="shrink-0 border-b border-gray-800 bg-gray-900/70 px-4 py-3">
        <div className="mb-3 flex items-end justify-between gap-3">
          <div>
            <h1 className="text-lg font-bold text-white">卡牌图鉴</h1>
            <p className="mt-0.5 text-xs text-gray-500">
              共 {cards.length} 张卡牌，当前显示 {filteredCards.length} 张
            </p>
          </div>
          {hasFilters && (
            <button
              type="button"
              onClick={resetFilters}
              className="shrink-0 text-xs text-gray-400 transition-colors hover:text-white"
            >
              重置筛选
            </button>
          )}
        </div>

        <div className="grid grid-cols-2 gap-2 sm:grid-cols-3 xl:grid-cols-[minmax(220px,2fr)_repeat(5,minmax(96px,1fr))]">
          <label className="col-span-2 sm:col-span-3 xl:col-span-1">
            <span className="sr-only">搜索卡名或卡号</span>
            <input
              type="search"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder="搜索卡名或卡号"
              className="h-9 w-full rounded-lg border border-gray-700 bg-gray-800 px-3 text-xs text-white outline-none transition-colors placeholder:text-gray-500 focus:border-orange-500"
            />
          </label>

          <FilterSelect label="卡集" value={filterSet} onChange={setFilterSet}>
            {ALL_SET_NAMES.map((setName) => (
              <option key={setName} value={setName}>
                {setName}
              </option>
            ))}
          </FilterSelect>

          <FilterSelect label="颜色" value={filterColor} onChange={setFilterColor}>
            {COLOR_OPTIONS.map((color) => (
              <option key={color} value={color}>
                {color}
              </option>
            ))}
          </FilterSelect>

          <FilterSelect label="类型" value={filterType} onChange={setFilterType}>
            {TYPE_OPTIONS.map((type) => (
              <option key={type.value} value={type.value}>
                {type.label}
              </option>
            ))}
          </FilterSelect>

          <FilterSelect label="稀有度" value={filterRarity} onChange={setFilterRarity}>
            {RARITY_OPTIONS.map((rarity) => (
              <option key={rarity} value={rarity}>
                {rarity}
              </option>
            ))}
          </FilterSelect>

          <FilterSelect label="费用" value={filterCost} onChange={setFilterCost}>
            {COST_OPTIONS.map((cost) => (
              <option key={cost} value={cost}>
                {cost}
              </option>
            ))}
          </FilterSelect>
        </div>
      </header>

      <div ref={containerRef} className="min-h-0 flex-1 overflow-y-auto px-3 pb-4 pt-3">
        {filteredCards.length === 0 ? (
          <div className="flex h-52 flex-col items-center justify-center gap-2">
            <p className="text-sm text-gray-500">没有找到符合条件的卡牌</p>
            <button
              type="button"
              onClick={resetFilters}
              className="text-xs text-orange-400 transition-colors hover:text-orange-300"
            >
              清除筛选条件
            </button>
          </div>
        ) : (
          <div className="relative" style={{ height: totalHeight }}>
            {visibleItems.map(({ index, row, col }) => {
              const card = filteredCards[index];
              if (!card) return null;

              return (
                <div
                  key={card.number}
                  className="absolute"
                  style={{
                    left: col * (CARD_WIDTH + CARD_GAP),
                    top: row * (CARD_HEIGHT + CARD_GAP),
                    width: CARD_WIDTH,
                    height: CARD_HEIGHT,
                  }}
                >
                  <CatalogCard card={card} onClick={() => setSelectedCard(card)} />
                </div>
              );
            })}
          </div>
        )}
      </div>

      <CardInfoPanel card={selectedCard} onClose={() => setSelectedCard(null)} />
    </section>
  );
}

function FilterSelect({
  label,
  value,
  onChange,
  children,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  children: React.ReactNode;
}) {
  return (
    <label>
      <span className="sr-only">{label}</span>
      <select
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="h-9 w-full rounded-lg border border-gray-700 bg-gray-800 px-2 text-xs text-gray-200 outline-none transition-colors focus:border-orange-500"
      >
        <option value="">{label}：全部</option>
        {children}
      </select>
    </label>
  );
}

function CatalogCard({ card, onClick }: { card: CardData; onClick: () => void }) {
  const rawSprite = card.sprite ?? card.image ?? "/sprites/CardBack.png";
  const [imageSrc, setImageSrc] = useState(thumbSrc(rawSprite));

  useEffect(() => {
    setImageSrc(thumbSrc(rawSprite));
  }, [rawSprite]);

  const handleImageError = () => {
    setImageSrc((current) => {
      if (current !== rawSprite) return rawSprite;
      if (card.image && current !== card.image) return card.image;
      return "/sprites/CardBack.png";
    });
  };

  return (
    <button
      type="button"
      onClick={onClick}
      className="group flex h-full w-full flex-col text-left"
      aria-label={`查看 ${card.name}（${card.number}）详情`}
    >
      <span className="relative block h-[164px] w-full overflow-hidden rounded-lg border border-gray-700 bg-gray-900 shadow-lg shadow-black/20 transition-all group-hover:-translate-y-0.5 group-hover:border-orange-400 group-hover:shadow-orange-950/40">
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img
          src={imageSrc}
          alt={card.name}
          loading="lazy"
          decoding="async"
          draggable={false}
          onError={handleImageError}
          className="h-full w-full object-cover"
        />
        {card.rarity && (
          <span className="absolute right-1 top-1 rounded bg-black/75 px-1.5 py-0.5 text-[9px] font-bold text-white">
            {card.rarity}
          </span>
        )}
        {card.sprites.length > 1 && (
          <span className="absolute bottom-1 right-1 rounded bg-black/75 px-1.5 py-0.5 text-[9px] font-bold text-orange-300">
            {card.sprites.length} 画
          </span>
        )}
      </span>
      <span className="mt-1 block w-full truncate text-[11px] font-medium text-gray-300 transition-colors group-hover:text-white">
        {card.name}
      </span>
      <span className="block text-[9px] text-gray-600">{card.number}</span>
    </button>
  );
}
