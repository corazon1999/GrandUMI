"use client";

import { useId } from "react";
import { motion } from "framer-motion";
import {
  ATTACK_ATTRIBUTE_THEMES,
  type AttackAttribute,
} from "@/lib/attackAttributeEffects";

interface AttributeAttackEffectProps {
  attributes: AttackAttribute[];
  path: string;
  reduceMotion: boolean;
}

function Traveler({ attribute, path, index, glowId }: { attribute: AttackAttribute; path: string; index: number; glowId: string }) {
  const theme = ATTACK_ATTRIBUTE_THEMES[attribute];
  const duration = Math.max(0.58, 0.8 + index * 0.08);
  const motionNode = (
    <animateMotion
      path={path}
      dur={`${duration}s`}
      begin={`${index * 0.1}s`}
      repeatCount="indefinite"
      rotate="auto"
    />
  );

  switch (attribute) {
    case "斩":
      return (
        <g filter={`url(#${glowId})`}>
          <path d="M -24 -2 L 16 0 L -16 3 Z" fill={theme.primary} opacity="0.96">{motionNode}</path>
        </g>
      );
    case "打":
      return (
        <g filter={`url(#${glowId})`}>
          <circle r="10" fill={`${theme.primary}66`} stroke={theme.accent} strokeWidth="2.5">{motionNode}</circle>
        </g>
      );
    case "射":
      return (
        <g filter={`url(#${glowId})`}>
          <path d="M -28 -2 L 13 -1 L 20 0 L 13 2 L -28 3 Z" fill={theme.primary}>{motionNode}</path>
        </g>
      );
    case "特":
      return (
        <g filter={`url(#${glowId})`}>
          <polygon points="0,-10 10,0 0,10 -10,0" fill={theme.primary} stroke={theme.accent} strokeWidth="2">{motionNode}</polygon>
        </g>
      );
    case "知":
      return (
        <g filter={`url(#${glowId})`}>
          <polygon points="0,-10 9,-5 9,5 0,10 -9,5 -9,-5" fill={`${theme.primary}70`} stroke={theme.accent} strokeWidth="2">{motionNode}</polygon>
        </g>
      );
    default:
      return (
        <g filter={`url(#${glowId})`}>
          <rect x="-8" y="-8" width="16" height="16" fill={theme.primary} stroke="#f472b6" strokeWidth="2">{motionNode}</rect>
        </g>
      );
  }
}

function AttributeTrail({ attribute, path, index, reduceMotion, glowId }: { attribute: AttackAttribute; path: string; index: number; reduceMotion: boolean; glowId: string }) {
  const theme = ATTACK_ATTRIBUTE_THEMES[attribute];
  const dashPattern = attribute === "射" ? "44 18" : attribute === "知" ? "8 11" : attribute === "?" ? "19 9 4 12" : "26 15";

  return (
    <g opacity={Math.max(0.62, 1 - index * 0.12)}>
      <path d={path} fill="none" stroke={theme.secondary} strokeWidth={18 - Math.min(7, index * 2)} strokeLinecap="round" opacity="0.07" filter={`url(#${glowId})`} />
      <path d={path} fill="none" stroke={theme.primary} strokeWidth={7 - Math.min(3, index)} strokeLinecap="round" opacity="0.2" filter={`url(#${glowId})`} />
      <motion.path
        d={path}
        fill="none"
        stroke={theme.primary}
        strokeWidth={attribute === "打" ? 3.4 : 2.2}
        strokeLinecap="round"
        strokeDasharray={dashPattern}
        initial={reduceMotion ? false : { strokeDashoffset: 0, opacity: 0 }}
        animate={reduceMotion ? { opacity: 0.76 } : { strokeDashoffset: -120, opacity: [0.55, 1, 0.7] }}
        transition={reduceMotion ? { duration: 0 } : { strokeDashoffset: { duration: 0.9 + index * 0.12, repeat: Infinity, ease: "linear" }, opacity: { duration: 0.7, repeat: Infinity, repeatType: "mirror" } }}
        filter={`url(#${glowId})`}
      />
      {!reduceMotion && (
        <>
          <Traveler attribute={attribute} path={path} index={index} glowId={glowId} />
          {Array.from({ length: 5 }, (_, particleIndex) => (
            <circle
              key={particleIndex}
              r={particleIndex % 2 ? 1.6 : 2.4}
              fill={particleIndex % 2 ? theme.secondary : theme.accent}
              opacity={0.45 + particleIndex * 0.08}
              filter={`url(#${glowId})`}
            >
              <animateMotion
                path={path}
                dur={`${1.05 + particleIndex * 0.14 + index * 0.08}s`}
                begin={`${particleIndex * -0.17 + index * 0.07}s`}
                repeatCount="indefinite"
              />
            </circle>
          ))}
        </>
      )}
    </g>
  );
}

/**
 * 每个攻击属性保留独立的透明轨迹、运动粒子和沿线辉光。
 * 多属性卡会逐层组合全部子属性，阻挡改向和响应式缩放仍由战斗关系层提供。
 */
export default function AttributeAttackEffect({
  attributes,
  path,
  reduceMotion,
}: AttributeAttackEffectProps) {
  const reactId = useId().replace(/:/g, "");
  const softGlowId = `attribute-soft-glow-${reactId}`;

  return (
    <g data-attack-vfx="procedural">
      <defs>
        <filter id={softGlowId} x="-100%" y="-100%" width="300%" height="300%" colorInterpolationFilters="sRGB">
          <feGaussianBlur stdDeviation="6" result="wideGlow" />
          <feMerge>
            <feMergeNode in="wideGlow" />
            <feMergeNode in="SourceGraphic" />
          </feMerge>
        </filter>
      </defs>

      {attributes.map((attribute, index) => (
        <AttributeTrail
          key={`trail:${attribute}`}
          attribute={attribute}
          path={path}
          index={index}
          reduceMotion={reduceMotion}
          glowId={softGlowId}
        />
      ))}
    </g>
  );
}
