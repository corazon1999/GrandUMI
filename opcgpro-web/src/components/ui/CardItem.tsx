"use client";

import { motion, AnimatePresence } from "framer-motion";
import NextImage from "next/image";
import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import type { CardData } from "@/types/card";
import { clsx } from "clsx";
import CardHoverPreview, { type HoverInfo } from "@/components/deck-editor/CardHoverPreview";
import CardZoomOverlay from "@/components/ui/CardZoomOverlay";

interface Props {
  card: CardData | null;
  isSelected?: boolean;
  isTapped?: boolean;
  powerBuff?: number;
  /** 费用修正（含持续光环，如 OP16-080 对方回合 +1）；正=升高(红) 负=降低(绿) */
  costBuff?: number;
  attachedDonCount?: number;
  faceDown?: boolean;
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
  /** 是否显示阻挡者「能量护盾」特效（仅场上角色区传 true，手牌/预览等不传） */
  showBlockerFx?: boolean;
  /** 快照下发的动态获得关键词（贴咚/条件/回合内授予的阻挡者等），与静态卡面 abilities 一并判定特效 */
  gainedKeywords?: string[];
  /** 攻击状态标识：can=可攻击 sick=本回合登场不可攻击 blocked=受到禁攻状态 none=不显示 */
  attackState?: "can" | "sick" | "blocked" | "none";
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
  hideCounter = false,
  counterValue,
  hidePower = false,
  hideCost = false,
  liftOnSelect = true,
  showBlockerFx = false,
  gainedKeywords,
  attackState = "none",
  battleHighlight,
  onClick,
  size = "md",
}: Props) {
  const showFaceDown = faceDown || !card;
  // 阻挡者：仅场上角色（showBlockerFx）且正面、静态能力或动态获得关键词含「阻挡者」时显示能量护盾特效
  // （动态授予如 OP16-073 回合末获得、OP15-053 贴咚获得——此前只看静态 abilities，玩家看不到任何变化，反馈#242/#253）
  const isBlocker =
    showBlockerFx &&
    !showFaceDown &&
    (!!card?.abilities?.includes("阻挡者") || !!gainedKeywords?.includes("阻挡者"));
  const donPower = attachedDonCount * 1000;
  const displayPower = (card?.power ?? 0) + powerBuff + donPower;
  const displayCost = Math.max(0, (card?.cost ?? 0) + costBuff);
  const displayCounter = counterValue ?? card?.counter ?? 0;
  const [imgSrc, setImgSrc] = useState(card?.sprite ?? "/sprites/CardBack.png");

  // 卡牌/异画变化时重新同步图源并重试加载（修复曾因服务器暂时不可用 onError 回退到
  // 卡背后、即使图恢复也一直卡在卡背的问题）
  useEffect(() => {
    setImgSrc(card?.sprite ?? "/sprites/CardBack.png");
  }, [card?.sprite]);

  // 悬停详情预览（仅正面且有卡牌数据时显示，避免泄露对手暗置手牌）
  const [hoverInfo, setHoverInfo] = useState<HoverInfo | null>(null);
  const hoverTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  // 右键大图详情（同样仅正面有卡牌时可开，避免泄露对手暗置牌）
  const [zoomOpen, setZoomOpen] = useState(false);

  useEffect(() => {
    return () => {
      if (hoverTimer.current) clearTimeout(hoverTimer.current);
    };
  }, []);

  const handleMouseEnter = (e: React.MouseEvent<HTMLDivElement>) => {
    if (showFaceDown || !card) return;
    const rect = e.currentTarget.getBoundingClientRect();
    if (hoverTimer.current) clearTimeout(hoverTimer.current);
    hoverTimer.current = setTimeout(
      () => setHoverInfo({ card, rect, currentSprite: imgSrc }),
      HOVER_DELAY,
    );
  };

  const handleMouseLeave = () => {
    if (hoverTimer.current) clearTimeout(hoverTimer.current);
    setHoverInfo(null);
  };

  // 右键 → 居中大图详情；屏蔽浏览器原生菜单，背面/无卡数据不弹（防泄露暗置牌）
  const handleContextMenu = (e: React.MouseEvent<HTMLDivElement>) => {
    e.preventDefault();
    if (showFaceDown || !card) return;
    if (hoverTimer.current) clearTimeout(hoverTimer.current);
    setHoverInfo(null);
    setZoomOpen(true);
  };

  return (
    <motion.div
      className={clsx(
        sizes[size],
        "relative shrink-0 cursor-pointer overflow-hidden rounded-md border-2 shadow-xl",
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
      onClick={onClick}
      onContextMenu={handleContextMenu}
      onMouseEnter={handleMouseEnter}
      onMouseLeave={handleMouseLeave}
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
            className={clsx(
              "object-cover transition-[filter] duration-300",
              isTapped && "grayscale brightness-[0.6]",
            )}
            draggable={false}
            onError={() =>
              setImgSrc((cur) =>
                card?.image && cur !== card.image ? card.image : "/sprites/CardBack.png",
              )
            }
          />
          {/* 费用 → 左上角（与卡面一致）；保留 costBuff 升降色。领袖无费用→隐藏 */}
          {!hideCost && (
            <span
              className={clsx(
                "absolute left-1 top-1 z-10 rounded bg-black/85 px-1.5 text-[11px] font-bold leading-tight ring-1 ring-white/15",
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

          {/* 阻挡者「能量护盾」特效：常驻辉光 + 旋转流光边框 + 立体盾牌徽标 */}
          {isBlocker && (
            <>
              {/* 打底常驻青光（呼吸） */}
              <div className="blocker-glow pointer-events-none absolute inset-0 z-10 rounded-md" />
              {/* 旋转流光边框（高光沿四边流动） */}
              <div className="blocker-aura pointer-events-none absolute inset-0 z-20 rounded-md" />
              {/* 名称栏旁的小盾牌徽标（贴卡面最底，不压插画） */}
              <span
                className="pointer-events-none absolute bottom-0.5 left-0.5 z-30 flex items-center justify-center rounded bg-gradient-to-br from-cyan-400/95 to-blue-700/95 p-[1.5px] shadow-[0_0_4px_rgba(34,211,238,0.8)] ring-1 ring-cyan-100/70"
                title="阻挡者"
              >
                {/* 背后柔和径向光晕 */}
                <span className="pointer-events-none absolute inset-0 -z-10 rounded bg-cyan-300/40 blur-[2px]" />
                <svg
                  viewBox="0 0 24 24"
                  className="h-2.5 w-2.5 drop-shadow-[0_0_1px_rgba(255,255,255,0.9)]"
                  fill="white"
                  stroke="rgba(255,255,255,0.95)"
                  strokeWidth="0.6"
                  aria-hidden
                >
                  <path d="M12 2 4 5v6c0 5 3.4 8.3 8 11 4.6-2.7 8-6 8-11V5l-8-3Z" />
                  {/* 盾面顶部高光 */}
                  <path
                    d="M12 4 6 6.3v4.7c0 1.4.4 2.6 1 3.7C8.4 11.5 10 8 12 4Z"
                    fill="rgba(255,255,255,0.45)"
                    stroke="none"
                  />
                </svg>
              </span>
            </>
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
                src="/status-icons/cannot-attack.png"
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
            {zoomOpen && card && (
              <CardZoomOverlay
                card={card}
                sprite={imgSrc}
                counterValue={displayCounter}
                onClose={() => setZoomOpen(false)}
              />
            )}
          </AnimatePresence>,
          document.body,
        )}
    </motion.div>
  );
}
