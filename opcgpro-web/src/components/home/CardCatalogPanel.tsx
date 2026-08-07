"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import type { CardData } from "@/types/card";
import { getAllCachedCards, loadAllCards } from "@/data/CardLoader";
import { useVirtualList } from "@/hooks/useVirtualList";
import { CARD_BACK_SRC, nextCardImageSrc, thumbSrc } from "@/lib/sprite";
import CardInfoPanel from "@/components/game/CardInfoPanel";
import {
  CARD_COSTS,
  CARD_PROPERTIES,
  CARD_RARITIES,
  CARD_SET_GROUPS,
  compareCatalogCards,
  filterAndSortCards,
} from "@/lib/cardSearch";

const TYPE_OPTIONS = [
  { value: "Leader", label: "领航" },
  { value: "Character", label: "角色" },
  { value: "Event", label: "事件" },
  { value: "Stage", label: "舞台" },
] as const;

const COLOR_OPTIONS = ["红", "绿", "蓝", "紫", "黑", "黄"];

const CARD_WIDTH = 118;
const CARD_HEIGHT = 198;
const CARD_GAP = 12;

type LoadState = "loading" | "done" | "error";

export default function CardCatalogPanel() {
  const [loadState, setLoadState] = useState<LoadState>("loading");
  const [cards, setCards] = useState<CardData[]>([]);
  const [selectedCard, setSelectedCard] = useState<CardData | null>(null);

  const [query, setQuery] = useState("");
  const [filterSets, setFilterSets] = useState<string[]>([]);
  const [filterColor, setFilterColor] = useState("");
  const [filterType, setFilterType] = useState("");
  const [filterProperty, setFilterProperty] = useState("");
  const [filterRarity, setFilterRarity] = useState("");
  const [filterCost, setFilterCost] = useState<number | null>(null);
  const [filterShowSub1, setFilterShowSub1] = useState(false);

  const loadCards = useCallback(async () => {
    setLoadState("loading");
    try {
      await loadAllCards();
      setCards(getAllCachedCards().slice());
      setLoadState("done");
    } catch {
      setLoadState("error");
    }
  }, []);

  useEffect(() => {
    void loadCards();
  }, [loadCards]);

  const filteredCards = useMemo(() => {
    return filterAndSortCards(
      cards,
      {
        searchQuery: query,
        filterColors: filterColor ? [filterColor] : [],
        filterType,
        filterProperty,
        filterRarity,
        filterCost,
        filterSets,
        filterShowSub1,
      },
      {
        includeLeadersWhenAllTypes: true,
        sortComparator: compareCatalogCards,
      },
    );
  }, [
    cards,
    query,
    filterColor,
    filterType,
    filterProperty,
    filterRarity,
    filterCost,
    filterSets,
    filterShowSub1,
  ]);

  const hasFilters = Boolean(
    query ||
      filterSets.length > 0 ||
      filterColor ||
      filterType ||
      filterProperty ||
      filterRarity ||
      filterCost !== null ||
      filterShowSub1,
  );

  const resetFilters = () => {
    setQuery("");
    setFilterSets([]);
    setFilterColor("");
    setFilterType("");
    setFilterProperty("");
    setFilterRarity("");
    setFilterCost(null);
    setFilterShowSub1(false);
  };

  const toggleFilterSet = (setName: string) => {
    setFilterSets((current) =>
      current.includes(setName)
        ? current.filter((name) => name !== setName)
        : [...current, setName],
    );
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
      <header className="shrink-0 border-b border-gray-800 bg-gray-900/70 px-3 py-3 @[640px]:px-4">
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
              className="min-h-11 shrink-0 rounded-lg px-3 text-sm text-gray-400 transition-colors hover:bg-gray-800 hover:text-white @[1024px]:min-h-0 @[1024px]:px-0 @[1024px]:text-xs"
            >
              重置筛选
            </button>
          )}
        </div>

        <div className="space-y-2 @[1024px]:hidden">
          <label>
            <span className="sr-only">搜索卡名、卡号或关键词</span>
            <input
              type="search"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder="搜索卡名、卡号或关键词"
              className="h-12 w-full rounded-xl border border-gray-700 bg-gray-800 px-3 text-base text-white outline-none transition-colors placeholder:text-gray-500 focus:border-orange-500"
            />
          </label>
          <details className="group rounded-xl border border-gray-800 bg-gray-950/50">
            <summary className="flex min-h-11 cursor-pointer list-none items-center justify-between px-3 text-sm font-medium text-gray-300 [&::-webkit-details-marker]:hidden">
              <span>筛选条件{hasFilters ? "（已启用）" : ""}</span>
              <span className="text-gray-500 transition-transform group-open:rotate-180">▾</span>
            </summary>
            <div className="grid grid-cols-2 gap-2 border-t border-gray-800 p-3">
              <CardSetFilter selectedSets={filterSets} onToggle={toggleFilterSet} onClear={() => setFilterSets([])} />
              <FilterSelect label="颜色" value={filterColor} onChange={setFilterColor}>
                {COLOR_OPTIONS.map((color) => <option key={color} value={color}>{color}</option>)}
              </FilterSelect>
              <FilterSelect label="类型" value={filterType} onChange={setFilterType}>
                {TYPE_OPTIONS.map((type) => <option key={type.value} value={type.value}>{type.label}</option>)}
              </FilterSelect>
              <FilterSelect label="属性" value={filterProperty} onChange={setFilterProperty}>
                {CARD_PROPERTIES.filter(Boolean).map((property) => <option key={property} value={property}>{property}</option>)}
              </FilterSelect>
              <FilterSelect label="稀有度" value={filterRarity} onChange={(rarity) => { setFilterRarity(rarity); if (rarity === "L") setFilterType("Leader"); }}>
                {CARD_RARITIES.filter(Boolean).map((rarity) => <option key={rarity} value={rarity}>{rarity}</option>)}
              </FilterSelect>
              <FilterSelect label="费用" value={filterCost ?? ""} onChange={(value) => setFilterCost(value === "" ? null : Number(value))}>
                {CARD_COSTS.map((cost) => <option key={cost} value={cost}>{cost}</option>)}
              </FilterSelect>
              <button
                type="button"
                onClick={() => setFilterShowSub1((current) => !current)}
                className={`col-span-2 min-h-11 rounded-lg border px-3 text-sm font-bold transition-colors ${
                  filterShowSub1 ? "border-blue-600 bg-blue-600/40 text-blue-200" : "border-gray-700 bg-gray-800 text-gray-500"
                }`}
              >
                {filterShowSub1 ? "✓ 显示角标 1 卡" : "显示角标 1 卡"}
              </button>
            </div>
          </details>
        </div>

        <div className="hidden gap-2 @[1024px]:grid @[1024px]:grid-cols-[minmax(220px,2fr)_repeat(6,minmax(96px,1fr))]">
          <label>
            <span className="sr-only">搜索卡名、卡号或关键词</span>
            <input
              type="search"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder="卡名 / 卡号 / 关键词"
              className="h-9 w-full rounded-lg border border-gray-700 bg-gray-800 px-3 text-xs text-white outline-none transition-colors placeholder:text-gray-500 focus:border-orange-500"
            />
          </label>

          <CardSetFilter
            selectedSets={filterSets}
            onToggle={toggleFilterSet}
            onClear={() => setFilterSets([])}
          />

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

          <FilterSelect label="属性" value={filterProperty} onChange={setFilterProperty}>
            {CARD_PROPERTIES.filter(Boolean).map((property) => (
              <option key={property} value={property}>
                {property}
              </option>
            ))}
          </FilterSelect>

          <FilterSelect
            label="稀有度"
            value={filterRarity}
            onChange={(rarity) => {
              setFilterRarity(rarity);
              if (rarity === "L") setFilterType("Leader");
            }}
          >
            {CARD_RARITIES.filter(Boolean).map((rarity) => (
              <option key={rarity} value={rarity}>
                {rarity}
              </option>
            ))}
          </FilterSelect>

          <FilterSelect
            label="费用"
            value={filterCost ?? ""}
            onChange={(value) => setFilterCost(value === "" ? null : Number(value))}
          >
            {CARD_COSTS.map((cost) => (
              <option key={cost} value={cost}>
                {cost}
              </option>
            ))}
          </FilterSelect>
        </div>

        <div className="mt-2 hidden flex-wrap items-center gap-2 @[1024px]:flex">
          <button
            type="button"
            onClick={() => setFilterShowSub1((current) => !current)}
            className={`rounded-lg border px-3 py-1.5 text-[10px] font-bold transition-colors ${
              filterShowSub1
                ? "border-blue-600 bg-blue-600/40 text-blue-200"
                : "border-gray-700 bg-gray-800 text-gray-500 hover:text-white"
            }`}
            title="角标 1 通常是旧环境或早期版本卡，默认隐藏"
          >
            {filterShowSub1 ? "✓ 显示角标 1 卡" : "已隐藏角标 1 卡"}
          </button>
          <span className="text-[10px] text-gray-600">
            排序：卡集顺序 · 卡号升序
          </span>
        </div>
      </header>

      <CatalogGrid
        cards={filteredCards}
        onSelect={setSelectedCard}
        onReset={resetFilters}
        resetSignal={[
          query,
          filterSets.join(","),
          filterColor,
          filterType,
          filterProperty,
          filterRarity,
          filterCost ?? "",
          String(filterShowSub1),
        ].join("\0")}
      />

      <CardInfoPanel
        card={selectedCard}
        onClose={() => setSelectedCard(null)}
        mobileSheet
        initialArtwork="latest"
      />
    </section>
  );
}

/**
 * 仅在卡牌数据加载完成后挂载网格，确保 useVirtualList 首次执行副作用时
 * 滚动容器已经存在，可以正确绑定 ResizeObserver 与 scroll 事件。
 */
function CatalogGrid({
  cards,
  onSelect,
  onReset,
  resetSignal,
}: {
  cards: CardData[];
  onSelect: (card: CardData) => void;
  onReset: () => void;
  resetSignal: string;
}) {
  const { containerRef, totalHeight, visibleItems } = useVirtualList({
    itemCount: cards.length,
    itemHeight: CARD_HEIGHT,
    rowWidth: CARD_WIDTH,
    columns: 1,
    gap: CARD_GAP,
    overscan: 3,
  });

  useEffect(() => {
    containerRef.current?.scrollTo({ top: 0 });
  }, [resetSignal, containerRef]);

  return (
    <div ref={containerRef} className="min-h-0 flex-1 overflow-y-auto px-3 pb-4 pt-3">
      {cards.length === 0 ? (
        <div className="flex h-52 flex-col items-center justify-center gap-2">
          <p className="text-sm text-gray-500">没有找到符合条件的卡牌</p>
          <button
            type="button"
            onClick={onReset}
            className="text-xs text-orange-400 transition-colors hover:text-orange-300"
          >
            清除筛选条件
          </button>
        </div>
      ) : (
        <div className="relative" style={{ height: totalHeight }}>
          {visibleItems.map(({ index, row, col }) => {
            const card = cards[index];
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
                <CatalogCard card={card} onClick={() => onSelect(card)} />
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

function CardSetFilter({
  selectedSets,
  onToggle,
  onClear,
}: {
  selectedSets: string[];
  onToggle: (setName: string) => void;
  onClear: () => void;
}) {
  return (
    <details className="group relative">
      <summary className="flex h-11 cursor-pointer list-none items-center justify-between rounded-lg border border-gray-700 bg-gray-800 px-2 text-sm text-gray-200 outline-none transition-colors hover:border-gray-600 focus:border-orange-500 @[1024px]:h-9 @[1024px]:text-xs [&::-webkit-details-marker]:hidden">
        <span>{selectedSets.length === 0 ? "弹数：全部" : `弹数：已选 ${selectedSets.length}`}</span>
        <span className="text-gray-500 transition-transform group-open:rotate-180">▾</span>
      </summary>
      <div className="absolute left-0 top-full z-40 mt-1 w-72 rounded-lg border border-gray-700 bg-gray-900 p-3 shadow-2xl">
        <div className="mb-2 flex items-center justify-between">
          <span className="text-[10px] font-bold text-gray-400">弹数（可多选）</span>
          {selectedSets.length > 0 && (
            <button
              type="button"
              onClick={onClear}
              className="text-[10px] text-gray-500 transition-colors hover:text-orange-400"
            >
              清空
            </button>
          )}
        </div>
        <div className="flex max-h-64 flex-col gap-2 overflow-y-auto pr-1">
          {CARD_SET_GROUPS.map((group) => (
            <div key={group.label}>
              <p className="mb-1 text-[9px] font-bold text-gray-600">{group.label}</p>
              <div className="flex flex-wrap gap-1">
                {group.sets.map((setName) => {
                  const selected = selectedSets.includes(setName);
                  return (
                    <button
                      key={setName}
                      type="button"
                      onClick={() => onToggle(setName)}
                      className={`min-h-9 rounded px-2 py-1 text-xs font-bold transition-colors ${
                        selected
                          ? "bg-orange-500 text-white"
                          : "bg-gray-800 text-gray-400 hover:text-white"
                      }`}
                    >
                      {setName}
                    </button>
                  );
                })}
              </div>
            </div>
          ))}
        </div>
      </div>
    </details>
  );
}

function FilterSelect({
  label,
  value,
  onChange,
  children,
}: {
  label: string;
  value: string | number;
  onChange: (value: string) => void;
  children: React.ReactNode;
}) {
  return (
    <label>
      <span className="sr-only">{label}</span>
      <select
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="h-11 w-full rounded-lg border border-gray-700 bg-gray-800 px-2 text-sm text-gray-200 outline-none transition-colors focus:border-orange-500 @[1024px]:h-9 @[1024px]:text-xs"
      >
        <option value="">{label}：全部</option>
        {children}
      </select>
    </label>
  );
}

function CatalogCard({ card, onClick }: { card: CardData; onClick: () => void }) {
  const rawSprite = card.sprites[card.sprites.length - 1] ?? card.sprite ?? card.image ?? CARD_BACK_SRC;
  const [imageSrc, setImageSrc] = useState(thumbSrc(rawSprite));

  useEffect(() => {
    setImageSrc(thumbSrc(rawSprite));
  }, [rawSprite]);

  const handleImageError = () => {
    setImageSrc((current) => nextCardImageSrc(current, rawSprite, card.image, "thumb"));
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
      <span className="mt-1 block w-full truncate text-xs font-medium text-gray-300 transition-colors group-hover:text-white">
        {card.name}
      </span>
      <span className="block text-xs text-gray-600">{card.number}</span>
    </button>
  );
}
