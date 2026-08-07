"use client";

import { motion, useReducedMotion } from "framer-motion";
import type { ReactNode } from "react";

const KEYWORD_ORDER = [
  "阻挡者",
  "速攻",
  "速攻：角色",
  "双重攻击",
  "不可阻挡",
  "流放",
  "可攻击活跃",
] as const;

type VisibleKeyword = (typeof KEYWORD_ORDER)[number];

interface Props {
  keywords: VisibleKeyword[];
}

interface BadgeTheme {
  background: string;
  border: string;
  glow: string;
}

const BADGE_THEMES: Record<VisibleKeyword, BadgeTheme> = {
  阻挡者: {
    background: "linear-gradient(145deg, rgba(34,211,238,.98), rgba(29,78,216,.98))",
    border: "rgba(207,250,254,.9)",
    glow: "0 0 5px rgba(34,211,238,.95)",
  },
  速攻: {
    background: "linear-gradient(145deg, rgba(251,191,36,.98), rgba(220,38,38,.98))",
    border: "rgba(254,240,138,.92)",
    glow: "0 0 5px rgba(251,146,60,.95)",
  },
  "速攻：角色": {
    background: "linear-gradient(145deg, rgba(253,224,71,.98), rgba(234,88,12,.98))",
    border: "rgba(254,249,195,.92)",
    glow: "0 0 5px rgba(250,204,21,.95)",
  },
  双重攻击: {
    background: "linear-gradient(145deg, rgba(244,114,182,.98), rgba(109,40,217,.98))",
    border: "rgba(251,207,232,.92)",
    glow: "0 0 5px rgba(217,70,239,.95)",
  },
  不可阻挡: {
    background: "linear-gradient(145deg, rgba(251,113,133,.98), rgba(153,27,27,.98))",
    border: "rgba(255,228,230,.92)",
    glow: "0 0 5px rgba(244,63,94,.95)",
  },
  流放: {
    background: "linear-gradient(145deg, rgba(192,132,252,.98), rgba(49,46,129,.98))",
    border: "rgba(243,232,255,.92)",
    glow: "0 0 5px rgba(168,85,247,.95)",
  },
  可攻击活跃: {
    background: "linear-gradient(145deg, rgba(52,211,153,.98), rgba(15,118,110,.98))",
    border: "rgba(209,250,229,.92)",
    glow: "0 0 5px rgba(16,185,129,.95)",
  },
};

/**
 * 合并卡面固有词条与服务端动态词条，只保留牌桌需要展示的战斗关键词。
 * 「速攻：角色」比普通「速攻」更具体，两者同时出现时只展示具体版本。
 */
export function resolveVisibleKeywords(
  abilities?: readonly string[],
  gainedKeywords?: readonly string[],
): VisibleKeyword[] {
  const allKeywords = new Set([...(abilities ?? []), ...(gainedKeywords ?? [])]);
  if (allKeywords.has("速攻：角色")) allKeywords.delete("速攻");
  return KEYWORD_ORDER.filter((keyword) => allKeywords.has(keyword));
}

function KeywordIcon({ keyword }: { keyword: VisibleKeyword }) {
  const commonProps = {
    viewBox: "0 0 24 24",
    className: "h-2.5 w-2.5 drop-shadow-[0_0_1px_rgba(255,255,255,0.95)]",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: 2,
    strokeLinecap: "round" as const,
    strokeLinejoin: "round" as const,
    "aria-hidden": true,
  };

  switch (keyword) {
    case "阻挡者":
      return (
        <svg {...commonProps} fill="currentColor" strokeWidth={0.6}>
          <path d="M12 2 4 5v6c0 5 3.4 8.3 8 11 4.6-2.7 8-6 8-11V5l-8-3Z" />
          <path d="M12 4 6 6.3v4.7c0 1.4.4 2.6 1 3.7C8.4 11.5 10 8 12 4Z" fill="rgba(255,255,255,0.45)" stroke="none" />
        </svg>
      );
    case "速攻":
      return (
        <svg {...commonProps} fill="currentColor" stroke="none">
          <path d="M13.4 1.8 4.8 13h5.7l-1 9.2L19.2 10h-5.8V1.8Z" />
        </svg>
      );
    case "速攻：角色":
      return (
        <svg {...commonProps} strokeWidth={1.8}>
          <circle cx="12" cy="12" r="8" />
          <path d="M12 1v4M12 19v4M1 12h4M19 12h4M13.2 5.8 8.5 12h3.2l-.7 6.1 5-7h-3.2l.4-5.3Z" fill="currentColor" stroke="none" />
        </svg>
      );
    case "双重攻击":
      return (
        <svg {...commonProps} strokeWidth={2.2}>
          <path d="m4 20 5.2-5.2M6 18l-2-2M14.8 9.2 20 4l-5.7 1.3-4.8 4.8" />
          <path d="m20 20-5.2-5.2M18 18l2-2M9.2 9.2 4 4l5.7 1.3 4.8 4.8" />
        </svg>
      );
    case "不可阻挡":
      return (
        <svg {...commonProps} strokeWidth={2}>
          <path d="M12 2 4.5 5v5.5c0 4.7 2.9 8 7.5 10.8 1.2-.7 2.2-1.5 3.1-2.3" />
          <path d="m15 6 5 5-5 5M9 11h11" />
        </svg>
      );
    case "流放":
      return (
        <svg {...commonProps} strokeWidth={2}>
          <path d="M12 3c5 0 8 3.4 8 8.1 0 5.2-3.7 9-8.7 9-4.2 0-7.3-2.9-7.3-6.6 0-3.3 2.5-5.8 5.8-5.8 2.8 0 4.7 1.8 4.7 4.1 0 1.9-1.4 3.3-3.1 3.3-1.4 0-2.4-.9-2.4-2" />
          <path d="m12 3-2.2 2.6M12 3l2.7 1.8" />
        </svg>
      );
    case "可攻击活跃":
      return (
        <svg {...commonProps} strokeWidth={1.8}>
          <circle cx="12" cy="12" r="7" />
          <circle cx="12" cy="12" r="2" fill="currentColor" stroke="none" />
          <path d="M12 1v4M12 19v4M1 12h4M19 12h4" />
        </svg>
      );
  }
}

function SoftGlow({
  color,
  reducedMotion,
  delay = 0,
}: {
  color: string;
  reducedMotion: boolean;
  delay?: number;
}) {
  return (
    <motion.span
      className="pointer-events-none absolute inset-0 z-10 rounded-md border"
      style={{ borderColor: color, boxShadow: `inset 0 0 9px ${color}, 0 0 4px ${color}` }}
      animate={reducedMotion ? { opacity: 0.42 } : { opacity: [0.25, 0.62, 0.25] }}
      transition={reducedMotion ? undefined : { duration: 2.2, delay, ease: "easeInOut", repeat: Infinity }}
    />
  );
}

function RushEffect({ reducedMotion, targeted }: { reducedMotion: boolean; targeted?: boolean }) {
  const color = targeted ? "rgba(250,204,21,.78)" : "rgba(251,146,60,.76)";
  return (
    <>
      <SoftGlow color={color} reducedMotion={reducedMotion} />
      {[0, 1].map((index) => (
        <motion.span
          key={index}
          className="pointer-events-none absolute -left-1/3 z-20 h-px w-1/2 rounded-full"
          style={{ top: index === 0 ? "28%" : "58%", rotate: "-18deg", background: `linear-gradient(90deg, transparent, ${color}, white, transparent)`, boxShadow: `0 0 4px ${color}` }}
          animate={reducedMotion ? { x: "170%", opacity: 0.55 } : { x: ["-40%", "290%"], opacity: [0, 0.9, 0] }}
          transition={reducedMotion ? undefined : { duration: 1.35, delay: index * 0.32, ease: "easeInOut", repeat: Infinity, repeatDelay: 0.5 }}
        />
      ))}
    </>
  );
}

function DoubleAttackEffect({ reducedMotion }: { reducedMotion: boolean }) {
  return (
    <>
      <SoftGlow color="rgba(217,70,239,.64)" reducedMotion={reducedMotion} />
      {[-1, 1].map((direction, index) => (
        <motion.span
          key={direction}
          className="pointer-events-none absolute z-20 h-[145%] w-px origin-center rounded-full bg-gradient-to-b from-transparent via-fuchsia-100 to-transparent"
          style={{ left: "50%", top: "-22%", rotate: `${direction * 29}deg`, boxShadow: "0 0 5px rgba(232,121,249,.9)" }}
          animate={reducedMotion ? { opacity: 0.45 } : { opacity: [0.12, 0.78, 0.12], scaleY: [0.82, 1.05, 0.82] }}
          transition={reducedMotion ? undefined : { duration: 1.7, delay: index * 0.22, ease: "easeInOut", repeat: Infinity }}
        />
      ))}
    </>
  );
}

function UnblockableEffect({ reducedMotion }: { reducedMotion: boolean }) {
  return (
    <>
      <SoftGlow color="rgba(244,63,94,.7)" reducedMotion={reducedMotion} />
      <motion.span
        className="pointer-events-none absolute z-20 h-3/4 w-3/4 rounded-md border border-rose-100/80"
        style={{ left: "12.5%", top: "12.5%", boxShadow: "0 0 7px rgba(244,63,94,.8), inset 0 0 7px rgba(251,113,133,.45)" }}
        animate={reducedMotion ? { opacity: 0.38 } : { opacity: [0, 0.68, 0], scale: [0.72, 1.12, 1.2] }}
        transition={reducedMotion ? undefined : { duration: 1.8, ease: "easeOut", repeat: Infinity }}
      />
    </>
  );
}

function BanishEffect({ reducedMotion }: { reducedMotion: boolean }) {
  return (
    <>
      <SoftGlow color="rgba(168,85,247,.65)" reducedMotion={reducedMotion} />
      <motion.span
        className="pointer-events-none absolute z-20 h-[115%] w-[150%] rounded-[50%] border border-violet-200/65"
        style={{ left: "-25%", top: "-7.5%", boxShadow: "0 0 9px rgba(139,92,246,.72), inset 0 0 9px rgba(76,29,149,.7)" }}
        animate={reducedMotion ? { opacity: 0.32 } : { opacity: [0.16, 0.58, 0.16], rotate: [0, 180, 360], scale: [0.92, 1.03, 0.92] }}
        transition={reducedMotion ? undefined : { duration: 4.6, ease: "linear", repeat: Infinity }}
      />
    </>
  );
}

function ActiveAttackEffect({ reducedMotion }: { reducedMotion: boolean }) {
  return (
    <>
      <SoftGlow color="rgba(16,185,129,.68)" reducedMotion={reducedMotion} />
      <motion.span
        className="pointer-events-none absolute inset-x-1 z-20 h-px bg-gradient-to-r from-transparent via-emerald-100 to-transparent"
        style={{ boxShadow: "0 0 5px rgba(52,211,153,.95)" }}
        animate={reducedMotion ? { top: "50%", opacity: 0.45 } : { top: ["8%", "92%", "8%"], opacity: [0.2, 0.75, 0.2] }}
        transition={reducedMotion ? undefined : { duration: 2.4, ease: "easeInOut", repeat: Infinity }}
      />
    </>
  );
}

function KeywordEffect({ keyword, reducedMotion }: { keyword: VisibleKeyword; reducedMotion: boolean }) {
  switch (keyword) {
    case "阻挡者":
      return (
        <>
          <div
            className="blocker-glow pointer-events-none absolute inset-0 z-10 rounded-md"
            style={reducedMotion ? { animation: "none", opacity: 0.65 } : undefined}
          />
          <div
            className="blocker-aura pointer-events-none absolute inset-0 z-20 rounded-md"
            style={reducedMotion ? { animation: "none" } : undefined}
          />
        </>
      );
    case "速攻":
      return <RushEffect reducedMotion={reducedMotion} />;
    case "速攻：角色":
      return <RushEffect reducedMotion={reducedMotion} targeted />;
    case "双重攻击":
      return <DoubleAttackEffect reducedMotion={reducedMotion} />;
    case "不可阻挡":
      return <UnblockableEffect reducedMotion={reducedMotion} />;
    case "流放":
      return <BanishEffect reducedMotion={reducedMotion} />;
    case "可攻击活跃":
      return <ActiveAttackEffect reducedMotion={reducedMotion} />;
  }
}

export default function CardKeywordEffects({ keywords }: Props) {
  const prefersReducedMotion = useReducedMotion();
  const reducedMotion = prefersReducedMotion ?? false;
  if (keywords.length === 0) return null;

  const effects: ReactNode[] = keywords.map((keyword) => (
    <KeywordEffect key={`effect-${keyword}`} keyword={keyword} reducedMotion={reducedMotion} />
  ));

  return (
    <>
      {effects}
      <div
        className={`pointer-events-none absolute bottom-0.5 left-0.5 z-30 flex ${keywords.length > 1 ? "flex-col -space-y-0.5" : "-space-x-0.5"}`}
        aria-label={`卡牌词条：${keywords.join("、")}`}
      >
        {keywords.map((keyword, index) => {
          const theme = BADGE_THEMES[keyword];
          return (
            <motion.span
              key={keyword}
              className="relative flex h-3.5 w-3.5 items-center justify-center rounded-[3px] text-white"
              style={{
                background: theme.background,
                border: `1px solid ${theme.border}`,
                boxShadow: theme.glow,
                zIndex: keywords.length - index,
              }}
              title={keyword}
              animate={reducedMotion ? undefined : { y: [0, -0.7, 0], filter: ["brightness(1)", "brightness(1.22)", "brightness(1)"] }}
              transition={reducedMotion ? undefined : { duration: 2, delay: index * 0.18, ease: "easeInOut", repeat: Infinity }}
            >
              <span className="pointer-events-none absolute inset-0 rounded-[2px] bg-gradient-to-br from-white/30 via-transparent to-black/20" />
              <KeywordIcon keyword={keyword} />
            </motion.span>
          );
        })}
      </div>
    </>
  );
}
