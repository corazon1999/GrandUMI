"use client";

import { motion, AnimatePresence } from "framer-motion";
import NextImage from "next/image";
import { useEffect, useRef, useState, type CSSProperties } from "react";
import { createPortal } from "react-dom";
import type { CardData } from "@/types/card";
import { clsx } from "clsx";
import CardHoverPreview, { type HoverInfo } from "@/components/deck-editor/CardHoverPreview";
import CardZoomOverlay from "@/components/ui/CardZoomOverlay";
import CardKeywordEffects, { resolveVisibleKeywords } from "@/components/ui/CardKeywordEffects";
import CardBack from "@/components/ui/CardBack";
import GameOverlayPortal from "@/components/ui/GameOverlayPortal";
import { createCardLongPressGesture } from "@/lib/cardLongPressGesture";
import { shouldShowDesktopCardHoverPreview } from "@/lib/cardHoverPlacement";
import { getLeaderBreathingEffect } from "@/lib/leaderBreathingEffects";
import { CARD_BACK_SRC, nextCardImageSrc, thumbSrc } from "@/lib/sprite";

interface Props {
  card: CardData | null;
  isSelected?: boolean;
  isTapped?: boolean;
  powerBuff?: number;
  /** 费用修正（含持续光环，如 OP16-080 对方回合 +1）；正=升高(红) 负=降低(绿) */
  costBuff?: number;
  attachedDonCount?: number;
  faceDown?: boolean;
  /** 暗置时使用的公开卡背外观；旧快照或无效值自动回退经典款。 */
  cardBackId?: string;
  /** 隐藏反击值徽标（卡牌在场上时反击值无意义，仅手牌防御时显示） */
  hideCounter?: boolean;
  /** 当前有效反击值（含场上静态效果）；未提供时使用卡牌印刷值 */
  counterValue?: number;
  /** 隐藏力量值（手牌中的角色不显示力量） */
  hidePower?: boolean;
  /** 隐藏费用值（领袖无费用） */
  hideCost?: boolean;
  /**
   * 选中时是否"浮起"（上移+较大放大）。默认 true（手牌等区域）。
   * 场上角色区容器为支持横向滚动用了 overflow 裁切，上移会被切顶，故传 false：
   * 仅靠黄框+抬层+轻放大标识选中，不向上溢出。
   */
  liftOnSelect?: boolean;
  /** 是否显示战斗关键词图标与特效（仅场上角色区传 true，手牌/预览等不传） */
  showKeywordFx?: boolean;
  /** 快照下发的动态获得关键词，与静态卡面 abilities 一并判定图标与特效 */
  gainedKeywords?: string[];
  /** 攻击状态标识：can=可攻击 sick=本回合登场不可攻击 blocked=受到禁攻状态 none=不显示 */
  attackState?: "can" | "sick" | "blocked" | "none";
  /** 卡牌至少一项【每回合1次】效果本回合仍可发动 */
  oncePerTurnEffectAvailable?: boolean;
  /** 战斗中的身份高亮；选框挂在卡牌旋转节点上，可自动适配活跃/横置形状 */
  battleHighlight?: "attacker" | "target" | "blocker";
  onClick?: () => void;
  size?: "sm" | "md" | "lg";
}

const sizes = {
  sm: "w-[4.5rem] h-[6.3rem]",
  md: "w-[6rem] h-[8.4rem]",
  lg: "w-[8rem] h-[11.2rem]",
};

const HOVER_DELAY = 180; // 悬停多少毫秒后显示详情（避免快速划过时闪烁）

export default function CardItem({
  card,
  isSelected = false,
  isTapped = false,
  powerBuff = 0,
  costBuff = 0,
  attachedDonCount = 0,
  faceDown = false,
  cardBackId,
  hideCounter = false,
  counterValue,
  hidePower = false,
  hideCost = false,
  liftOnSelect = true,
  showKeywordFx = false,
  gainedKeywords,
  attackState = "none",
  oncePerTurnEffectAvailable = false,
  battleHighlight,
  onClick,
  size = "md",
}: Props) {
  const showFaceDown = faceDown || !card;
  // 仅场上正面角色展示；同时识别卡面固有词条和服务端快照下发的动态词条。
  const visibleKeywords =
    showKeywordFx && !showFaceDown
      ? resolveVisibleKeywords(card?.abilities, gainedKeywords)
      : [];
  const donPower = attachedDonCount * 1000;
  const displayPower = (card?.power ?? 0) + powerBuff + donPower;
  const displayCost = Math.max(0, (card?.cost ?? 0) + costBuff);
  const displayCounter = counterValue ?? card?.counter ?? 0;
  const rawSprite = card?.sprite ?? CARD_BACK_SRC;
  const [imgSrc, setImgSrc] = useState(thumbSrc(rawSprite));
  const [imageFailed, setImageFailed] = useState(false);
  const leaderBreathingEffect =
    !showFaceDown && card?.type === "Leader"
      ? getLeaderBreathingEffect(card.number, rawSprite)
      : null;
  const leaderBreathingStyle = leaderBreathingEffect
    ? ({
        "--leader-breath-focus-x": leaderBreathingEffect.focusX,
        "--leader-breath-focus-y": leaderBreathingEffect.focusY,
        "--leader-breath-duration": leaderBreathingEffect.duration,
        "--leader-breath-scale": leaderBreathingEffect.scale,
        "--leader-breath-lift": leaderBreathingEffect.lift,
        "--leader-subject-x": leaderBreathingEffect.subjectX,
        "--leader-subject-y": leaderBreathingEffect.subjectY,
        "--leader-subject-radius-x": leaderBreathingEffect.subjectRadiusX,
        "--leader-subject-radius-y": leaderBreathingEffect.subjectRadiusY,
        "--leader-energy-duration": leaderBreathingEffect.energyDuration,
        "--leader-breath-primary": leaderBreathingEffect.primaryRgb,
        "--leader-breath-secondary": leaderBreathingEffect.secondaryRgb,
      } as CSSProperties)
    : undefined;

  // 卡牌/异画变化时重新同步图源并重试加载（修复曾因服务器暂时不可用 onError 回退到
  // 卡背后、即使图恢复也一直卡在卡背的问题）
  useEffect(() => {
    setImgSrc(thumbSrc(rawSprite));
    setImageFailed(false);
  }, [card?.sprite]);

  // 悬停详情预览（仅正面且有卡牌数据时显示，避免泄露对手暗置手牌）
  const [hoverInfo, setHoverInfo] = useState<HoverInfo | null>(null);
  const hoverTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  // 右键或触屏长按大图详情；保存打开时的卡牌身份，避免卡牌切换瞬间展示新牌信息。
  const [zoomCardIdentity, setZoomCardIdentity] = useState<string | null>(null);
  const cardIdentity = showFaceDown || !card ? null : `${card.number}\u0000${rawSprite}`;
  const currentCardRef = useRef({ card, cardIdentity, showFaceDown });
  currentCardRef.current = { card, cardIdentity, showFaceDown };

  const clearHoverPreview = () => {
    if (hoverTimer.current) clearTimeout(hoverTimer.current);
    hoverTimer.current = null;
    setHoverInfo(null);
  };

  const [longPressGesture] = useState(() => createCardLongPressGesture({
    onLongPress: (pressedIdentity) => {
      const current = currentCardRef.current;
      if (current.showFaceDown || !current.card || current.cardIdentity !== pressedIdentity) return;
      clearHoverPreview();
      setZoomCardIdentity(pressedIdentity);
    },
  }));

  useEffect(() => {
    const handleWindowPointerMove = (event: PointerEvent) => {
      longPressGesture.move(event);
    };
    const handleWindowPointerUp = (event: PointerEvent) => {
      const result = longPressGesture.finish(event);
      if (result === "ignored") return;
      clearHoverPreview();
      if (result === "long-press") event.preventDefault();
    };
    const handleWindowPointerCancel = (event: PointerEvent) => {
      const result = longPressGesture.cancelPointer(event);
      if (result !== "ignored") clearHoverPreview();
    };
    const handleWindowClick = (event: MouseEvent) => {
      if (!longPressGesture.consumeSuppressedClick(event)) return;
      event.preventDefault();
      event.stopImmediatePropagation();
    };

    // HandArea 会把触控指针捕获到外层；窗口捕获阶段仍能稳定看到移动和释放。
    window.addEventListener("pointermove", handleWindowPointerMove, true);
    window.addEventListener("pointerup", handleWindowPointerUp, true);
    window.addEventListener("pointercancel", handleWindowPointerCancel, true);
    window.addEventListener("click", handleWindowClick, true);
    return () => {
      window.removeEventListener("pointermove", handleWindowPointerMove, true);
      window.removeEventListener("pointerup", handleWindowPointerUp, true);
      window.removeEventListener("pointercancel", handleWindowPointerCancel, true);
      window.removeEventListener("click", handleWindowClick, true);
      longPressGesture.dispose();
      if (hoverTimer.current) clearTimeout(hoverTimer.current);
    };
  }, [longPressGesture]);

  useEffect(() => {
    longPressGesture.cancelActive();
    clearHoverPreview();
    setZoomCardIdentity(null);
  }, [cardIdentity, longPressGesture]);

  const handlePointerEnter = (e: React.PointerEvent<HTMLDivElement>) => {
    if (!shouldShowDesktopCardHoverPreview(e.pointerType) || showFaceDown || !card || !cardIdentity) return;
    const rect = e.currentTarget.getBoundingClientRect();
    if (hoverTimer.current) clearTimeout(hoverTimer.current);
    const hoveredIdentity = cardIdentity;
    hoverTimer.current = setTimeout(
      () => {
        hoverTimer.current = null;
        if (currentCardRef.current.cardIdentity !== hoveredIdentity) return;
        setHoverInfo({ card, rect, currentSprite: rawSprite });
      },
      HOVER_DELAY,
    );
  };

  const handlePointerLeave = (e: React.PointerEvent<HTMLDivElement>) => {
    if (e.pointerType === "mouse") clearHoverPreview();
  };

  const handlePointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    if (e.pointerType === "mouse") {
      longPressGesture.noteMousePointerDown(e, e.button);
      return;
    }
    if (!e.isPrimary || showFaceDown || !cardIdentity) return;
    clearHoverPreview();
    // 不在按下时 preventDefault，保留普通短按和非 HandArea 场景的页面滚动。
    longPressGesture.start(e, cardIdentity);
  };

  const handleClick = (e: React.MouseEvent<HTMLDivElement>) => {
    // 即使没有业务 onClick，也保留节点级兜底，避免合成 click 冒泡到牌区祖先。
    if (longPressGesture.consumeSuppressedClick(e)) {
      e.preventDefault();
      e.stopPropagation();
      e.nativeEvent.stopImmediatePropagation();
      return;
    }
    onClick?.();
  };

  // 右键 → 居中大图详情；屏蔽浏览器原生菜单，背面/无卡数据不弹（防泄露暗置牌）
  const handleContextMenu = (e: React.MouseEvent<HTMLDivElement>) => {
    e.preventDefault();
    // 部分浏览器把触摸长按产生的 contextmenu 标成 mouse，故按近期触控坐标判定。
    if (longPressGesture.shouldSuppressContextMenu(e)) {
      e.stopPropagation();
      return;
    }
    if (showFaceDown || !card || !cardIdentity) return;
    clearHoverPreview();
    setZoomCardIdentity(cardIdentity);
  };

  return (
    <motion.div
      className={clsx(
        sizes[size],
        "relative shrink-0 cursor-pointer select-none overflow-hidden rounded-md border-2 shadow-xl",
        "transform-gpu backface-hidden transition-colors",
        isSelected
          ? "z-30 border-yellow-300"
          : "border-slate-500/70 hover:border-slate-200",
        battleHighlight === "attacker"
          ? "ring-4 ring-red-500 shadow-red-500/50"
          : battleHighlight === "target"
            ? "ring-4 ring-amber-400 shadow-amber-400/50"
            : battleHighlight === "blocker"
              ? "ring-4 ring-cyan-300 shadow-cyan-400/60"
              : isSelected
                ? "shadow-yellow-300/40"
                : "shadow-black/35",
      )}
      animate={{
        rotate: isTapped ? 90 : 0,
        y: isSelected && liftOnSelect ? -12 : 0,
        scale: isSelected ? (liftOnSelect ? 1.05 : 1.03) : 1,
      }}
      transition={{ type: "spring", stiffness: 300, damping: 25 }}
      style={{ WebkitTouchCallout: "none" }}
      data-game-board-interactive={onClick ? "true" : undefined}
      onClick={handleClick}
      onContextMenu={handleContextMenu}
      onPointerDown={handlePointerDown}
      onPointerEnter={handlePointerEnter}
      onPointerLeave={handlePointerLeave}
      whileHover={!isSelected ? { scale: 1.03 } : {}}
    >
      {showFaceDown ? (
        <CardBack cardBackId={cardBackId} />
      ) : (
        <>
          {!imageFailed ? (
            <NextImage
              src={imgSrc}
              alt={card!.name}
              fill
              sizes="180px"
              className={clsx(
                "object-cover transition-[filter] duration-300",
                isTapped && "grayscale brightness-[0.6]",
              )}
              draggable={false}
              onError={() => setImgSrc((cur) => {
                const next = nextCardImageSrc(cur, rawSprite, card?.image, "thumb");
                if (next === cur || next.includes(CARD_BACK_SRC)) setImageFailed(true);
                return next;
              })}
            />
          ) : (
            <div className={clsx(
              "absolute inset-0 flex flex-col bg-gradient-to-b from-slate-800 to-slate-950 p-1 text-slate-100",
              isTapped && "grayscale brightness-[0.6]",
            )}>
              <p className="line-clamp-2 text-[9px] font-black leading-tight">{card!.name}</p>
              <p className="mt-0.5 text-[7px] text-slate-400">{card!.number}</p>
              <p className="mt-1 line-clamp-6 overflow-hidden whitespace-pre-wrap text-[7px] leading-tight text-slate-200">
                {card!.effectEvent || card!.trigger || card!.abilities.join(" / ") || "卡图暂不可用"}
              </p>
            </div>
          )}
          {leaderBreathingEffect && (
            <div
              className={clsx("leader-breath-fx", isTapped && "leader-breath-fx--tapped")}
              style={leaderBreathingStyle}
              aria-hidden="true"
            >
              {/* 背景层做轻微反向景深；卡框、技能文字和名称栏继续使用静态原图。 */}
              <div className="leader-breath-art-viewport">
                <NextImage
                  src={imgSrc}
                  alt=""
                  fill
                  sizes="180px"
                  className="leader-breath-depth object-cover"
                  draggable={false}
                />
              </div>
              {/* 柔边人物层承担主要呼吸，让脸部和肩部从背景中“浮”出来。 */}
              <div className="leader-breath-subject-viewport">
                <NextImage
                  src={imgSrc}
                  alt=""
                  fill
                  sizes="180px"
                  className="leader-breath-subject object-cover"
                  draggable={false}
                />
              </div>
              <span className="leader-breath-gaze" />
              <span className="leader-breath-sheen" />
              <span className="leader-breath-energy" />
              <span className="leader-breath-frame" />
            </div>
          )}
          {/* 费用 → 左上角（与卡面一致）；保留 costBuff 升降色。领袖无费用→隐藏 */}
          {!hideCost && (
            <span
              className={clsx(
                "absolute left-[2px] top-[2px] z-10 rounded bg-black/85 px-1.5 text-[11px] font-bold leading-tight ring-1 ring-white/15",
                costBuff > 0
                  ? "text-orange-300"
                  : costBuff < 0
                    ? "text-green-300"
                    : "text-white",
              )}
            >
              {displayCost}
            </span>
          )}

          {/* 贴咚数量 → 右上角 */}
          {attachedDonCount > 0 && (
            <span className="absolute right-1 top-1 z-10 rounded bg-yellow-300 px-1 text-[10px] font-black leading-tight text-black ring-1 ring-black/10">
              DONx{attachedDonCount}
            </span>
          )}

          {/* 反击值 → 底部右侧（避开左下角阻挡者盾牌）；仅手牌防御时显示 */}
          {displayCounter > 0 && !hideCounter && (
            <span className="absolute bottom-1 right-1 z-10 rounded bg-amber-500/90 px-1 text-[10px] font-black leading-tight text-black shadow ring-1 ring-black/20">
              反{displayCounter}
            </span>
          )}

          {/* 力量 → 底部居中；手牌中的角色不显示力量。
              按卡类型判断而非 power>0：基础0力量角色(如OP16-034路飞)、被效果改成负数力量的角色也须显示当前力量。 */}
          {!hidePower && (card!.type === "Character" || card!.type === "Leader") && (
            <span
              className={clsx(
                "absolute bottom-1 left-1/2 z-10 -translate-x-1/2 rounded bg-black/85 px-1.5 text-[11px] font-bold leading-tight ring-1 ring-white/15",
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

          <CardKeywordEffects keywords={visibleKeywords} />

          {oncePerTurnEffectAvailable && (
            <span
              data-once-per-turn-ready="true"
              className="pointer-events-none absolute right-0 top-1/2 z-30 flex -translate-y-1/2 items-center gap-0.5 rounded-l bg-gradient-to-l from-cyan-400 to-blue-600 px-1 py-0.5 shadow-[0_0_7px_rgba(34,211,238,0.95)] ring-1 ring-cyan-100/80"
              title="每回合1次效果可发动"
              aria-label="每回合1次效果可发动"
            >
              <svg viewBox="0 0 24 24" className="h-3 w-3 text-white" fill="none" aria-hidden>
                <path d="M19 7v5h-5M5 17v-5h5" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" />
                <path d="M18.2 11a7 7 0 0 0-12-3M5.8 13a7 7 0 0 0 12 3" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" />
              </svg>
              <span className="text-[8px] font-black leading-none text-white">1</span>
            </span>
          )}

          {/* 攻击状态标识 → 左侧边中部（避开所有现有徽标），仅我方回合显示 */}
          {attackState === "can" && (
            <span
              className="pointer-events-none absolute left-0 top-1/2 z-30 flex -translate-y-1/2 items-center rounded-r bg-gradient-to-r from-emerald-400 to-green-600 px-0.5 py-1 shadow-[0_0_6px_rgba(16,185,129,0.95)] ring-1 ring-emerald-100/70 animate-pulse"
              title="可攻击"
            >
              <svg viewBox="0 0 24 24" className="h-3 w-3 text-white drop-shadow-[0_0_1px_rgba(0,0,0,0.6)]" fill="currentColor" aria-hidden>
                <path d="M12 4l6 7h-4v7h-4v-7H6z" />
              </svg>
            </span>
          )}
          {attackState === "sick" && (
            <span
              className="pointer-events-none absolute left-0 top-1/2 z-30 flex -translate-y-1/2 items-center rounded-r bg-slate-800/85 px-1 py-0.5 ring-1 ring-slate-400/50"
              title="本回合登场，不可攻击"
            >
              <span className="text-[8px] font-black leading-none tracking-tight text-sky-300/90">Zzz</span>
            </span>
          )}
          {attackState === "blocked" && (
            <span
              className="pointer-events-none absolute left-0 top-1/2 z-30 flex -translate-y-1/2 items-center rounded-r bg-slate-950/90 p-0.5 shadow-[0_0_6px_rgba(239,68,68,0.9)] ring-1 ring-red-400/80"
              title="无法攻击"
            >
              <NextImage
                src="/status-icons/cannot-attack.webp"
                alt=""
                width={24}
                height={24}
                className="h-5 w-5 object-contain"
              />
            </span>
          )}
        </>
      )}

      {/* 悬停详情浮窗：portal 到 body，fixed 定位且不拦截鼠标，避免被牌桌 overflow 裁剪 */}
      {typeof document !== "undefined" &&
        createPortal(
          <AnimatePresence>
            {hoverInfo && <CardHoverPreview info={hoverInfo} counterValue={displayCounter} />}
          </AnimatePresence>,
          document.body,
        )}
      <GameOverlayPortal>
        <AnimatePresence>
          {zoomCardIdentity !== null && zoomCardIdentity === cardIdentity && card && !showFaceDown && (
            <CardZoomOverlay
              card={card}
              sprite={rawSprite}
              counterValue={displayCounter}
              onClose={() => setZoomCardIdentity(null)}
            />
          )}
        </AnimatePresence>
      </GameOverlayPortal>
    </motion.div>
  );
}
