"use client";

import { useEffect, useState } from "react";
import type { CardData } from "@/types/card";
import Modal from "@/components/ui/Modal";
import { toDisplayColor, primaryDisplayColor, COLOR_STYLES } from "@/lib/colorMap";
import { CARD_BACK_SRC, displaySrc, nextCardImageSrc } from "@/lib/sprite";

interface Props {
  card: CardData | null;
  onClose: () => void;
  mobileSheet?: boolean;
  /** 组牌页手机竖屏使用较小卡图，避免详情占满视口。 */
  compactMobile?: boolean;
  initialArtwork?: "default" | "latest";
}

const TYPE_LABELS: Record<string, string> = {
  Leader:    "领航",
  Character: "角色",
  Stage:     "舞台",
  Event:     "事件",
};

export default function CardInfoPanel({
  card,
  onClose,
  mobileSheet = false,
  compactMobile = false,
  initialArtwork = "default",
}: Props) {
  return (
    <Modal
      open={!!card}
      onClose={onClose}
      title={card?.name}
      maxWidthClass="w-[calc(100vw-2rem)] max-w-4xl"
      mobileSheet={mobileSheet}
    >
      {card && (
        <CardInfoContent
          card={card}
          initialArtwork={initialArtwork}
          compactMobile={compactMobile}
        />
      )}
    </Modal>
  );
}

function CardInfoContent({
  card,
  initialArtwork,
  compactMobile,
}: {
  card: CardData;
  initialArtwork: NonNullable<Props["initialArtwork"]>;
  compactMobile: boolean;
}) {
  const displayColor = toDisplayColor(card.color);
  const primary      = primaryDisplayColor(card.color);
  const colorStyle   = COLOR_STYLES[primary];
  const sprites = card.sprites?.length
    ? card.sprites
    : [card.sprite ?? card.image ?? CARD_BACK_SRC];
  const initialSpriteIndex = initialArtwork === "latest" ? sprites.length - 1 : 0;
  const [spriteIndex, setSpriteIndex] = useState(initialSpriteIndex);
  const [imageSrc, setImageSrc] = useState(displaySrc(sprites[initialSpriteIndex]));

  useEffect(() => {
    setSpriteIndex(initialSpriteIndex);
    setImageSrc(displaySrc(sprites[initialSpriteIndex]));
    // 卡号或入口默认画面变化时，重置为对应版本
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [card.number, initialArtwork]);

  const selectSprite = (index: number) => {
    setSpriteIndex(index);
    setImageSrc(displaySrc(sprites[index]));
  };

  const moveSprite = (direction: -1 | 1) => {
    const next = (spriteIndex + direction + sprites.length) % sprites.length;
    selectSprite(next);
  };

  return (
    <div className="max-h-[78vh] overflow-y-auto pr-1">
      <div className="flex flex-col items-center gap-5 lg:flex-row lg:items-start">
        {/* 卡图 */}
        <div
          data-card-info-image
          className={`relative aspect-[0.717] shrink-0 overflow-hidden rounded-lg bg-gray-800 ${
            compactMobile
              ? "w-[min(62vw,14rem)] sm:w-full sm:max-w-[22rem]"
              : "w-full max-w-[22rem]"
          }`}
        >
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src={imageSrc}
            alt={card.name}
            className="w-full h-full object-cover"
            onError={() =>
              setImageSrc((current) => {
                const rawSprite = sprites[spriteIndex] ?? CARD_BACK_SRC;
                return nextCardImageSrc(current, rawSprite, card.image, "display");
              })
            }
          />
          {/* 颜色条 */}
          {colorStyle && (
            <div className={`absolute bottom-0 left-0 right-0 h-1 ${colorStyle.bg}`} />
          )}

          {sprites.length > 1 && (
            <>
              <button
                type="button"
                onClick={() => moveSprite(-1)}
                aria-label="上一张异画"
                className="absolute bottom-1/2 left-2 flex h-9 w-9 translate-y-1/2 items-center justify-center rounded-full bg-black/70 text-xl text-white transition-colors hover:bg-black/90"
              >
                ‹
              </button>
              <button
                type="button"
                onClick={() => moveSprite(1)}
                aria-label="下一张异画"
                className="absolute bottom-1/2 right-2 flex h-9 w-9 translate-y-1/2 items-center justify-center rounded-full bg-black/70 text-xl text-white transition-colors hover:bg-black/90"
              >
                ›
              </button>
              <span className="absolute bottom-3 left-1/2 -translate-x-1/2 rounded-full bg-black/75 px-2 py-0.5 text-xs font-bold text-white">
                {spriteIndex + 1} / {sprites.length}
              </span>
            </>
          )}
        </div>

        {/* 卡片信息 */}
        <div className="flex w-full min-w-0 flex-col gap-3 text-base">
          {/* 编号 + 颜色 + 类型 */}
          <div className="flex flex-wrap gap-1.5 items-center">
            <span className="text-orange-400 font-bold text-sm">{card.number}</span>
            <span className={`text-sm font-bold px-2 py-0.5 rounded ${colorStyle?.bg ?? "bg-gray-700"} text-white`}>
              {displayColor}
            </span>
            <span className="text-gray-400 text-sm">
              {TYPE_LABELS[card.type] ?? card.type}
            </span>
          </div>

          {/* 稀有度 + 角标 */}
          <div className="flex flex-wrap gap-1.5 items-center">
            {card.rarity && (
              <span className={`text-xs font-bold px-2 py-0.5 rounded ${
                card.rarity === 'L'   ? 'bg-yellow-500 text-black' :
                card.rarity === 'SR'  ? 'bg-pink-500 text-white' :
                card.rarity === 'R'   ? 'bg-sky-500 text-white' :
                card.rarity === 'UC'  ? 'bg-gray-500 text-white' :
                card.rarity === 'U'   ? 'bg-gray-500 text-white' :
                card.rarity === 'C'   ? 'bg-gray-700 text-gray-300' :
                card.rarity === 'SEC' ? 'bg-red-600 text-white' :
                card.rarity === 'P'   ? 'bg-emerald-500 text-white' :
                'bg-gray-700 text-white'
              }`}>
                {card.rarity === 'L' ? '领袖' :
                 card.rarity === 'SR' ? '超稀有' :
                 card.rarity === 'R' ? '稀有' :
                 card.rarity === 'UC' ? '罕见' :
                card.rarity === 'U' ? '罕见' :
                 card.rarity === 'C' ? '普通' :
                 card.rarity === 'SEC' ? '隐藏稀有' :
                 card.rarity === 'P' ? '宣传' : card.rarity}
              </span>
            )}
            {card.subscript > 0 && (
              <span className="text-yellow-400 text-sm font-bold">角标 {card.subscript}</span>
            )}
          </div>

          {/* 属性 */}
          {card.property && (
            <p className="text-gray-400 text-sm">
              属性 <span className="text-white">{card.property}</span>
            </p>
          )}

          {/* 威力 */}
          {card.power > 0 && (
            <p className="text-sm">
              <span className="text-gray-400 mr-1">威力</span>
              <span className="text-white font-bold">{card.power.toLocaleString()}</span>
            </p>
          )}

          {/* 费用 */}
          {card.cost > 0 && (
            <p className="text-sm">
              <span className="text-gray-400 mr-1">费用</span>
              <span className="text-white font-bold">{card.cost}</span>
            </p>
          )}

          {/* 反击 */}
          {card.counter > 0 && (
            <p className="text-sm">
              <span className="text-gray-400 mr-1">反击</span>
              <span className="text-white font-bold">+{card.counter}</span>
            </p>
          )}

          {/* 关键词 */}
          {card.keyWords.length > 0 && (
            <div>
              <p className="mb-1 text-xs font-bold text-gray-500">特征</p>
              <div className="flex flex-wrap gap-1">
                {card.keyWords.map((k) => (
                  <span key={k} className="text-xs bg-blue-900/60 text-blue-300 px-2 py-0.5 rounded">
                    {k}
                  </span>
                ))}
              </div>
            </div>
          )}

          {card.abilities.length > 0 && (
            <div>
              <p className="mb-1 text-xs font-bold text-gray-500">能力</p>
              <div className="flex flex-wrap gap-1">
                {card.abilities.map((ability) => (
                  <span
                    key={ability}
                    className="rounded bg-emerald-900/60 px-2 py-0.5 text-xs text-emerald-300"
                  >
                    {ability}
                  </span>
                ))}
              </div>
            </div>
          )}

          {card.trigger && (
            <div className="rounded-lg border border-amber-800/50 bg-amber-950/30 p-3">
              <p className="mb-1 text-xs font-bold text-amber-400">触发</p>
              <p className="whitespace-pre-wrap text-sm leading-relaxed text-amber-100">
                {card.trigger}
              </p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
