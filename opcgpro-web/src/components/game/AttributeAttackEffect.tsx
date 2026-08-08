"use client";

import { Fragment } from "react";
import { motion } from "framer-motion";
import {
  ATTACK_ATTRIBUTE_THEMES,
  type AttackAttribute,
} from "@/lib/attackAttributeEffects";

interface Point {
  x: number;
  y: number;
}

interface AttributeAttackEffectProps {
  attributes: AttackAttribute[];
  path: string;
  source: Point;
  target: Point;
  reduceMotion: boolean;
}

function polygonPoints(center: Point, radius: number, sides: number, rotation = -90) {
  return Array.from({ length: sides }, (_, index) => {
    const angle = ((rotation + (360 / sides) * index) * Math.PI) / 180;
    return `${center.x + Math.cos(angle) * radius},${center.y + Math.sin(angle) * radius}`;
  }).join(" ");
}

function animationState(reduceMotion: boolean, delay: number, scale = 1) {
  return {
    initial: reduceMotion ? false : { opacity: 0, scale: scale * 0.45 },
    animate: reduceMotion
      ? { opacity: 0.72, scale }
      : { opacity: [0, 1, 0.76], scale: [scale * 0.45, scale * 1.18, scale] },
    transition: { duration: reduceMotion ? 0 : 0.62, delay, ease: "easeOut" as const },
  };
}

function SlashImpact({ center, color, reduceMotion, delay }: ImpactProps) {
  return (
    <motion.g {...animationState(reduceMotion, delay)} style={{ transformOrigin: `${center.x}px ${center.y}px` }}>
      <motion.path
        d={`M ${center.x - 34} ${center.y + 25} L ${center.x + 35} ${center.y - 28}`}
        fill="none"
        stroke={color}
        strokeWidth="6"
        strokeLinecap="round"
        initial={reduceMotion ? false : { pathLength: 0, opacity: 0 }}
        animate={{ pathLength: 1, opacity: reduceMotion ? 0.75 : [0, 1, 0.82] }}
        transition={{ duration: reduceMotion ? 0 : 0.28, delay }}
      />
      <motion.path
        d={`M ${center.x - 28} ${center.y - 30} L ${center.x + 30} ${center.y + 25}`}
        fill="none"
        stroke={ATTACK_ATTRIBUTE_THEMES.斩.secondary}
        strokeWidth="3"
        strokeLinecap="round"
        initial={reduceMotion ? false : { pathLength: 0, opacity: 0 }}
        animate={{ pathLength: 1, opacity: reduceMotion ? 0.7 : [0, 1, 0.7] }}
        transition={{ duration: reduceMotion ? 0 : 0.26, delay: delay + 0.08 }}
      />
    </motion.g>
  );
}

interface ImpactProps {
  center: Point;
  color: string;
  reduceMotion: boolean;
  delay: number;
}

function StrikeImpact({ center, color, reduceMotion, delay }: ImpactProps) {
  const spokes = [0, 45, 90, 135];
  return (
    <motion.g {...animationState(reduceMotion, delay, 0.92)} style={{ transformOrigin: `${center.x}px ${center.y}px` }}>
      {[20, 38].map((radius, index) => (
        <motion.circle
          key={radius}
          cx={center.x}
          cy={center.y}
          r={radius}
          fill="none"
          stroke={index === 0 ? color : ATTACK_ATTRIBUTE_THEMES.打.secondary}
          strokeWidth={index === 0 ? 7 : 3}
          initial={reduceMotion ? false : { r: 8, opacity: 0 }}
          animate={{ r: radius, opacity: reduceMotion ? 0.55 : [0, 0.95, 0.52] }}
          transition={{ duration: reduceMotion ? 0 : 0.5, delay: delay + index * 0.06 }}
        />
      ))}
      {spokes.map((angle) => {
        const radians = (angle * Math.PI) / 180;
        const dx = Math.cos(radians) * 48;
        const dy = Math.sin(radians) * 48;
        return (
          <line
            key={angle}
            x1={center.x - dx}
            y1={center.y - dy}
            x2={center.x + dx}
            y2={center.y + dy}
            stroke={color}
            strokeWidth="3"
            strokeLinecap="round"
            opacity="0.72"
          />
        );
      })}
    </motion.g>
  );
}

function ShotImpact({ center, color, reduceMotion, delay }: ImpactProps) {
  return (
    <motion.g {...animationState(reduceMotion, delay, 0.86)} style={{ transformOrigin: `${center.x}px ${center.y}px` }}>
      <circle cx={center.x} cy={center.y} r="28" fill="none" stroke={color} strokeWidth="3" strokeDasharray="8 5" />
      <circle cx={center.x} cy={center.y} r="8" fill="none" stroke={ATTACK_ATTRIBUTE_THEMES.射.accent} strokeWidth="3" />
      <path
        d={`M ${center.x - 42} ${center.y} H ${center.x - 13} M ${center.x + 13} ${center.y} H ${center.x + 42} M ${center.x} ${center.y - 42} V ${center.y - 13} M ${center.x} ${center.y + 13} V ${center.y + 42}`}
        fill="none"
        stroke={color}
        strokeWidth="3"
        strokeLinecap="round"
      />
    </motion.g>
  );
}

function SpecialImpact({ center, color, reduceMotion, delay }: ImpactProps) {
  const lightning = [
    [center.x - 39, center.y + 10],
    [center.x - 12, center.y - 34],
    [center.x - 5, center.y - 8],
    [center.x + 26, center.y - 30],
    [center.x + 10, center.y + 4],
    [center.x + 38, center.y + 18],
    [center.x + 2, center.y + 17],
    [center.x - 11, center.y + 39],
    [center.x - 16, center.y + 12],
  ].map((point) => point.join(",")).join(" ");

  return (
    <motion.g {...animationState(reduceMotion, delay, 0.94)} style={{ transformOrigin: `${center.x}px ${center.y}px` }}>
      <polygon points={lightning} fill={`${color}55`} stroke={color} strokeWidth="3" strokeLinejoin="round" />
      <motion.circle
        cx={center.x}
        cy={center.y}
        r="33"
        fill="none"
        stroke={ATTACK_ATTRIBUTE_THEMES.特.secondary}
        strokeWidth="3"
        strokeDasharray="5 8"
        animate={reduceMotion ? undefined : { rotate: 360 }}
        transition={reduceMotion ? undefined : { duration: 1.6, repeat: Infinity, ease: "linear" }}
        style={{ transformOrigin: `${center.x}px ${center.y}px` }}
      />
    </motion.g>
  );
}

function KnowledgeImpact({ center, color, reduceMotion, delay }: ImpactProps) {
  const outer = polygonPoints(center, 39, 6);
  const inner = polygonPoints(center, 20, 6, -60);
  return (
    <motion.g {...animationState(reduceMotion, delay, 0.9)} style={{ transformOrigin: `${center.x}px ${center.y}px` }}>
      <polygon points={outer} fill={`${color}18`} stroke={color} strokeWidth="3" />
      <polygon points={inner} fill="none" stroke={ATTACK_ATTRIBUTE_THEMES.知.secondary} strokeWidth="3" />
      {[0, 120, 240].map((angle) => {
        const radians = (angle * Math.PI) / 180;
        const x = center.x + Math.cos(radians) * 30;
        const y = center.y + Math.sin(radians) * 30;
        return <circle key={angle} cx={x} cy={y} r="4" fill={ATTACK_ATTRIBUTE_THEMES.知.accent} />;
      })}
      <path d={`M ${center.x - 31} ${center.y} H ${center.x + 31}`} stroke={color} strokeWidth="2" opacity="0.7" />
    </motion.g>
  );
}

function UnknownImpact({ center, color, reduceMotion, delay }: ImpactProps) {
  const fragments = [
    [-31, -25, 13],
    [23, -31, 9],
    [-37, 17, 8],
    [28, 18, 14],
  ];
  return (
    <motion.g {...animationState(reduceMotion, delay, 0.92)} style={{ transformOrigin: `${center.x}px ${center.y}px` }}>
      <circle cx={center.x - 4} cy={center.y} r="27" fill="none" stroke="#22d3ee" strokeWidth="4" opacity="0.55" />
      <circle cx={center.x + 4} cy={center.y} r="27" fill="none" stroke="#f472b6" strokeWidth="4" opacity="0.5" />
      <path d={`M ${center.x - 26} ${center.y} H ${center.x + 27}`} stroke={color} strokeWidth="5" strokeDasharray="11 7" />
      {fragments.map(([dx, dy, size], index) => (
        <rect
          key={`${dx}:${dy}`}
          x={center.x + dx}
          y={center.y + dy}
          width={size}
          height={Math.max(5, size / 2)}
          fill={index % 2 === 0 ? color : ATTACK_ATTRIBUTE_THEMES["?"].secondary}
          opacity="0.72"
          transform={`rotate(${index % 2 === 0 ? -18 : 21} ${center.x + dx} ${center.y + dy})`}
        />
      ))}
    </motion.g>
  );
}

function ImpactGlyph({ attribute, center, reduceMotion, delay }: ImpactProps & { attribute: AttackAttribute }) {
  const color = ATTACK_ATTRIBUTE_THEMES[attribute].primary;
  const props = { center, color, reduceMotion, delay };
  switch (attribute) {
    case "斩": return <SlashImpact {...props} />;
    case "打": return <StrikeImpact {...props} />;
    case "射": return <ShotImpact {...props} />;
    case "特": return <SpecialImpact {...props} />;
    case "知": return <KnowledgeImpact {...props} />;
    default: return <UnknownImpact {...props} />;
  }
}

function Traveler({ attribute, path, index }: { attribute: AttackAttribute; path: string; index: number }) {
  const theme = ATTACK_ATTRIBUTE_THEMES[attribute];
  const duration = 0.82 + index * 0.12;
  const commonMotion = (
    <animateMotion
      path={path}
      dur={`${duration}s`}
      begin={`${index * 0.11}s`}
      repeatCount="indefinite"
      rotate="auto"
    />
  );

  switch (attribute) {
    case "斩":
      return <path d="M -15 0 L 12 0" stroke={theme.primary} strokeWidth="4" strokeLinecap="round">{commonMotion}</path>;
    case "打":
      return <circle r="7" fill={theme.primary} stroke={theme.accent} strokeWidth="2">{commonMotion}</circle>;
    case "射":
      return <path d="M -13 0 L 10 0" stroke={theme.primary} strokeWidth="5" strokeLinecap="round">{commonMotion}</path>;
    case "特":
      return <polygon points="0,-7 7,0 0,7 -7,0" fill={theme.primary} stroke={theme.accent} strokeWidth="2">{commonMotion}</polygon>;
    case "知":
      return <polygon points="0,-7 6,-3 6,3 0,7 -6,3 -6,-3" fill={`${theme.primary}88`} stroke={theme.accent} strokeWidth="2">{commonMotion}</polygon>;
    default:
      return <rect x="-6" y="-6" width="12" height="12" fill={theme.primary} stroke="#f472b6" strokeWidth="2">{commonMotion}</rect>;
  }
}

/** 为每个子属性同时渲染独立弹道单元和命中纹样，形成可辨识的复合攻击。 */
export default function AttributeAttackEffect({
  attributes,
  path,
  source,
  target,
  reduceMotion,
}: AttributeAttackEffectProps) {
  const midpoint = {
    x: source.x + (target.x - source.x) * 0.52,
    y: source.y + (target.y - source.y) * 0.52,
  };

  return (
    <g>
      {!reduceMotion && attributes.map((attribute, index) => (
        <Fragment key={`traveler:${attribute}`}>
          <Traveler attribute={attribute} path={path} index={index} />
          {attributes.length > 1 && (
            <circle
              cx={midpoint.x + index * 7}
              cy={midpoint.y - index * 5}
              r={3 + index}
              fill={ATTACK_ATTRIBUTE_THEMES[attribute].primary}
              opacity="0.74"
            />
          )}
        </Fragment>
      ))}

      {attributes.map((attribute, index) => (
        <ImpactGlyph
          key={`impact:${attribute}`}
          attribute={attribute}
          center={target}
          color={ATTACK_ATTRIBUTE_THEMES[attribute].primary}
          reduceMotion={reduceMotion}
          delay={index * 0.08}
        />
      ))}
    </g>
  );
}
