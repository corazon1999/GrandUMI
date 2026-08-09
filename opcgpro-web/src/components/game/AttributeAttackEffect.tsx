"use client";

import { Fragment, useId } from "react";
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

interface ImpactProps {
  center: Point;
  color: string;
  reduceMotion: boolean;
  delay: number;
  filterId: string;
}

const PARTICLE_ANGLES = [-158, -132, -102, -72, -40, -15, 17, 43, 71, 104, 137, 168] as const;

const ATTACK_ATTRIBUTE_TEXTURES: Record<AttackAttribute, string> = {
  斩: "/vfx/attack-slash.webp",
  打: "/vfx/attack-strike.webp",
  射: "/vfx/attack-shot.webp",
  特: "/vfx/attack-special.webp",
  知: "/vfx/attack-knowledge.webp",
  "?": "/vfx/attack-unknown.webp",
};

function polygonPoints(center: Point, radius: number, sides: number, rotation = -90) {
  return Array.from({ length: sides }, (_, index) => {
    const angle = ((rotation + (360 / sides) * index) * Math.PI) / 180;
    return `${center.x + Math.cos(angle) * radius},${center.y + Math.sin(angle) * radius}`;
  }).join(" ");
}

function pointAt(center: Point, angle: number, radius: number) {
  const radians = (angle * Math.PI) / 180;
  return {
    x: center.x + Math.cos(radians) * radius,
    y: center.y + Math.sin(radians) * radius,
  };
}

function burstState(reduceMotion: boolean, delay: number, scale = 1) {
  return {
    initial: reduceMotion ? false : { opacity: 0, scale: scale * 0.32 },
    animate: reduceMotion
      ? { opacity: 0.8, scale }
      : { opacity: [0, 1, 0.82], scale: [scale * 0.32, scale * 1.22, scale] },
    transition: { duration: reduceMotion ? 0 : 0.62, delay, ease: "easeOut" as const },
  };
}

function ImpactLight({ center, color, filterId }: Pick<ImpactProps, "center" | "color" | "filterId">) {
  return (
    <g>
      <circle cx={center.x} cy={center.y} r="74" fill={color} opacity="0.055" filter={`url(#${filterId})`} />
      <circle cx={center.x} cy={center.y} r="38" fill={color} opacity="0.1" filter={`url(#${filterId})`} />
    </g>
  );
}

function SlashImpact({ center, color, reduceMotion, delay, filterId }: ImpactProps) {
  const slashes = [
    { d: `M ${center.x - 48} ${center.y + 34} Q ${center.x} ${center.y + 1} ${center.x + 48} ${center.y - 37}`, width: 7, offset: 0 },
    { d: `M ${center.x - 39} ${center.y - 39} Q ${center.x - 2} ${center.y - 5} ${center.x + 42} ${center.y + 35}`, width: 4.5, offset: 0.07 },
    { d: `M ${center.x - 51} ${center.y + 21} Q ${center.x - 4} ${center.y - 3} ${center.x + 40} ${center.y - 27}`, width: 2, offset: 0.12 },
  ];

  return (
    <motion.g {...burstState(reduceMotion, delay)} style={{ transformOrigin: `${center.x}px ${center.y}px` }}>
      <ImpactLight center={center} color={color} filterId={filterId} />
      <ellipse cx={center.x} cy={center.y} rx="48" ry="22" fill="#7dd3fc" opacity="0.1" filter={`url(#${filterId})`} />
      {slashes.map((slash) => (
        <Fragment key={slash.d}>
          <motion.path
            d={slash.d}
            fill="none"
            stroke="#38bdf8"
            strokeWidth={slash.width + 8}
            strokeLinecap="round"
            opacity="0.2"
            filter={`url(#${filterId})`}
            initial={reduceMotion ? false : { pathLength: 0 }}
            animate={{ pathLength: 1 }}
            transition={{ duration: reduceMotion ? 0 : 0.22, delay: delay + slash.offset }}
          />
          <motion.path
            d={slash.d}
            fill="none"
            stroke={slash.width > 5 ? "#f8fafc" : color}
            strokeWidth={slash.width}
            strokeLinecap="round"
            initial={reduceMotion ? false : { pathLength: 0, opacity: 0 }}
            animate={{ pathLength: 1, opacity: 0.95 }}
            transition={{ duration: reduceMotion ? 0 : 0.18, delay: delay + slash.offset }}
          />
        </Fragment>
      ))}
      {PARTICLE_ANGLES.slice(0, 9).map((angle, index) => {
        const start = pointAt(center, angle, 20);
        const end = pointAt(center, angle, 46 + (index % 3) * 9);
        return (
          <motion.path
            key={angle}
            d={`M ${start.x} ${start.y} L ${end.x} ${end.y}`}
            stroke={index % 2 ? "#38bdf8" : "#e0f2fe"}
            strokeWidth={index % 3 === 0 ? 2.4 : 1.3}
            strokeLinecap="round"
            initial={reduceMotion ? false : { opacity: 0, pathLength: 0 }}
            animate={{ opacity: reduceMotion ? 0.7 : [0, 1, 0.24], pathLength: 1 }}
            transition={{ duration: reduceMotion ? 0 : 0.42, delay: delay + 0.08 + index * 0.018 }}
          />
        );
      })}
    </motion.g>
  );
}

function StrikeImpact({ center, color, reduceMotion, delay, filterId }: ImpactProps) {
  return (
    <motion.g {...burstState(reduceMotion, delay, 0.96)} style={{ transformOrigin: `${center.x}px ${center.y}px` }}>
      <ImpactLight center={center} color={color} filterId={filterId} />
      {[18, 34, 54].map((radius, index) => (
        <motion.ellipse
          key={radius}
          cx={center.x}
          cy={center.y}
          rx={radius * 1.15}
          ry={radius * 0.72}
          fill={index === 0 ? `${color}38` : "none"}
          stroke={index === 1 ? color : ATTACK_ATTRIBUTE_THEMES.打.secondary}
          strokeWidth={index === 0 ? 7 : Math.max(1.5, 4 - index)}
          opacity={0.9 - index * 0.18}
          filter={index < 2 ? `url(#${filterId})` : undefined}
          initial={reduceMotion ? false : { rx: 7, ry: 5, opacity: 0 }}
          animate={{ rx: radius * 1.15, ry: radius * 0.72, opacity: 0.78 - index * 0.13 }}
          transition={{ duration: reduceMotion ? 0 : 0.48 + index * 0.08, delay: delay + index * 0.04 }}
        />
      ))}
      {PARTICLE_ANGLES.map((angle, index) => {
        const start = pointAt(center, angle, 16);
        const end = pointAt(center, angle, 44 + (index % 4) * 8);
        const debris = pointAt(center, angle + 5, 34 + (index % 3) * 7);
        return (
          <Fragment key={angle}>
            <motion.line
              x1={start.x}
              y1={start.y}
              x2={end.x}
              y2={end.y}
              stroke={index % 2 ? color : "#fff7c2"}
              strokeWidth={index % 3 === 0 ? 3 : 1.6}
              strokeLinecap="round"
              initial={reduceMotion ? false : { opacity: 0, pathLength: 0 }}
              animate={{ opacity: reduceMotion ? 0.62 : [0, 0.95, 0.22], pathLength: 1 }}
              transition={{ duration: reduceMotion ? 0 : 0.48, delay: delay + index * 0.012 }}
            />
            {index % 2 === 0 && (
              <motion.polygon
                points={`${debris.x - 3},${debris.y - 2} ${debris.x + 4},${debris.y} ${debris.x - 1},${debris.y + 4}`}
                fill={index % 4 === 0 ? "#d97706" : "#fde68a"}
                initial={reduceMotion ? false : { opacity: 0, scale: 0 }}
                animate={{ opacity: reduceMotion ? 0.66 : [0, 0.9, 0.38], scale: [0.2, 1.2, 1] }}
                transition={{ duration: reduceMotion ? 0 : 0.54, delay: delay + 0.08 + index * 0.015 }}
              />
            )}
          </Fragment>
        );
      })}
      <circle cx={center.x} cy={center.y} r="8" fill="#fff7c2" opacity="0.94" filter={`url(#${filterId})`} />
    </motion.g>
  );
}

function ShotImpact({ center, color, reduceMotion, delay, filterId }: ImpactProps) {
  return (
    <motion.g {...burstState(reduceMotion, delay, 0.9)} style={{ transformOrigin: `${center.x}px ${center.y}px` }}>
      <ImpactLight center={center} color={color} filterId={filterId} />
      {[14, 28, 43].map((radius, index) => (
        <motion.circle
          key={radius}
          cx={center.x}
          cy={center.y}
          r={radius}
          fill={index === 0 ? `${color}25` : "none"}
          stroke={index === 1 ? "#ffffff" : color}
          strokeWidth={index === 0 ? 4 : 2.2}
          strokeDasharray={index === 2 ? "4 7" : undefined}
          opacity={0.95 - index * 0.18}
          filter={index === 0 ? `url(#${filterId})` : undefined}
          initial={reduceMotion ? false : { r: 5, opacity: 0 }}
          animate={{ r: radius, opacity: 0.88 - index * 0.14 }}
          transition={{ duration: reduceMotion ? 0 : 0.36 + index * 0.08, delay: delay + index * 0.035 }}
        />
      ))}
      <path
        d={`M ${center.x - 58} ${center.y} H ${center.x - 15} M ${center.x + 15} ${center.y} H ${center.x + 58} M ${center.x} ${center.y - 58} V ${center.y - 15} M ${center.x} ${center.y + 15} V ${center.y + 58}`}
        fill="none"
        stroke={color}
        strokeWidth="2.4"
        strokeLinecap="round"
        filter={`url(#${filterId})`}
      />
      {PARTICLE_ANGLES.filter((_, index) => index % 2 === 0).map((angle, index) => {
        const start = pointAt(center, angle, 10);
        const end = pointAt(center, angle, 38 + index * 4);
        return (
          <motion.line
            key={angle}
            x1={start.x}
            y1={start.y}
            x2={end.x}
            y2={end.y}
            stroke={index % 2 ? "#f59e0b" : "#fff7c2"}
            strokeWidth="1.7"
            initial={reduceMotion ? false : { opacity: 0, pathLength: 0 }}
            animate={{ opacity: reduceMotion ? 0.7 : [0, 1, 0.24], pathLength: 1 }}
            transition={{ duration: reduceMotion ? 0 : 0.34, delay: delay + 0.06 + index * 0.02 }}
          />
        );
      })}
      <circle cx={center.x} cy={center.y} r="5" fill="#ffffff" filter={`url(#${filterId})`} />
    </motion.g>
  );
}

function SpecialImpact({ center, color, reduceMotion, delay, filterId }: ImpactProps) {
  const lightning = [
    [center.x - 49, center.y + 12],
    [center.x - 17, center.y - 42],
    [center.x - 8, center.y - 12],
    [center.x + 32, center.y - 38],
    [center.x + 12, center.y + 2],
    [center.x + 48, center.y + 21],
    [center.x + 5, center.y + 19],
    [center.x - 14, center.y + 48],
    [center.x - 19, center.y + 15],
  ].map((point) => point.join(",")).join(" ");

  return (
    <motion.g {...burstState(reduceMotion, delay, 0.94)} style={{ transformOrigin: `${center.x}px ${center.y}px` }}>
      <ImpactLight center={center} color={color} filterId={filterId} />
      <polygon points={lightning} fill={`${color}38`} stroke={color} strokeWidth="3" strokeLinejoin="round" filter={`url(#${filterId})`} />
      {[32, 47].map((radius, index) => (
        <motion.ellipse
          key={radius}
          cx={center.x}
          cy={center.y}
          rx={radius}
          ry={radius * 0.52}
          fill="none"
          stroke={index === 0 ? ATTACK_ATTRIBUTE_THEMES.特.secondary : color}
          strokeWidth={index === 0 ? 3 : 1.5}
          strokeDasharray={index === 0 ? "5 7" : "2 9"}
          opacity={0.9 - index * 0.18}
          animate={reduceMotion ? undefined : { rotate: index === 0 ? 360 : -360 }}
          transition={reduceMotion ? undefined : { duration: 1.4 + index * 0.5, repeat: Infinity, ease: "linear" }}
          style={{ transformOrigin: `${center.x}px ${center.y}px` }}
        />
      ))}
      {[-1, 1].map((direction) => (
        <motion.path
          key={direction}
          d={`M ${center.x - 56} ${center.y + direction * 10} L ${center.x - 35} ${center.y - direction * 13} L ${center.x - 16} ${center.y + direction * 5} L ${center.x + 4} ${center.y - direction * 18} L ${center.x + 27} ${center.y + direction * 7} L ${center.x + 55} ${center.y - direction * 12}`}
          fill="none"
          stroke={direction > 0 ? "#f5d0fe" : "#c084fc"}
          strokeWidth={direction > 0 ? 2.2 : 1.4}
          strokeLinecap="round"
          strokeLinejoin="round"
          filter={`url(#${filterId})`}
          initial={reduceMotion ? false : { opacity: 0, pathLength: 0 }}
          animate={{ opacity: reduceMotion ? 0.74 : [0.2, 1, 0.45], pathLength: 1 }}
          transition={{ duration: reduceMotion ? 0 : 0.28, delay: delay + (direction > 0 ? 0.04 : 0.11) }}
        />
      ))}
      {PARTICLE_ANGLES.filter((_, index) => index % 2 === 1).map((angle, index) => {
        const point = pointAt(center, angle, 45 + (index % 3) * 7);
        return <circle key={angle} cx={point.x} cy={point.y} r={index % 3 === 0 ? 3 : 1.8} fill={index % 2 ? "#f472b6" : "#f5d0fe"} opacity="0.82" />;
      })}
    </motion.g>
  );
}

function KnowledgeImpact({ center, color, reduceMotion, delay, filterId }: ImpactProps) {
  const outer = polygonPoints(center, 48, 6);
  const middle = polygonPoints(center, 33, 6, -60);
  const inner = polygonPoints(center, 18, 6);

  return (
    <motion.g {...burstState(reduceMotion, delay, 0.92)} style={{ transformOrigin: `${center.x}px ${center.y}px` }}>
      <ImpactLight center={center} color={color} filterId={filterId} />
      <polygon points={outer} fill={`${color}0f`} stroke={color} strokeWidth="2.5" filter={`url(#${filterId})`} />
      <motion.polygon
        points={middle}
        fill={`${ATTACK_ATTRIBUTE_THEMES.知.secondary}22`}
        stroke={ATTACK_ATTRIBUTE_THEMES.知.secondary}
        strokeWidth="2"
        animate={reduceMotion ? undefined : { rotate: -360 }}
        transition={reduceMotion ? undefined : { duration: 3.2, repeat: Infinity, ease: "linear" }}
        style={{ transformOrigin: `${center.x}px ${center.y}px` }}
      />
      <polygon points={inner} fill="none" stroke="#ecfeff" strokeWidth="2.2" />
      {[0, 60, 120, 180, 240, 300].map((angle, index) => {
        const outerPoint = pointAt(center, angle, 48);
        const node = pointAt(center, angle, 32);
        return (
          <Fragment key={angle}>
            <line x1={node.x} y1={node.y} x2={outerPoint.x} y2={outerPoint.y} stroke={color} strokeWidth="1" opacity="0.58" />
            <circle cx={node.x} cy={node.y} r={index % 2 ? 2.5 : 3.8} fill="#ecfeff" filter={`url(#${filterId})`} />
          </Fragment>
        );
      })}
      <motion.rect
        x={center.x - 47}
        y={center.y - 3}
        width="94"
        height="6"
        fill="#67e8f9"
        opacity="0.26"
        filter={`url(#${filterId})`}
        animate={reduceMotion ? undefined : { y: [center.y - 34, center.y + 30, center.y - 34], opacity: [0.12, 0.5, 0.12] }}
        transition={reduceMotion ? undefined : { duration: 1.25, repeat: Infinity, ease: "easeInOut" }}
      />
      {[-43, -29, 27, 42].map((dx, index) => (
        <rect key={dx} x={center.x + dx} y={center.y + (index % 2 ? 26 : -31)} width={index % 2 ? 8 : 13} height="2" fill={index % 2 ? "#2563eb" : "#67e8f9"} opacity="0.68" />
      ))}
    </motion.g>
  );
}

function UnknownImpact({ center, color, reduceMotion, delay, filterId }: ImpactProps) {
  const fragments = [
    [-44, -35, 13, -18],
    [31, -39, 9, 21],
    [-50, 22, 8, -12],
    [37, 26, 14, 17],
    [-11, -52, 7, 32],
    [4, 44, 11, -28],
  ] as const;

  return (
    <motion.g {...burstState(reduceMotion, delay, 0.92)} style={{ transformOrigin: `${center.x}px ${center.y}px` }}>
      <ImpactLight center={center} color="#22d3ee" filterId={filterId} />
      <circle cx={center.x - 5} cy={center.y} r="34" fill="none" stroke="#22d3ee" strokeWidth="5" opacity="0.62" filter={`url(#${filterId})`} />
      <circle cx={center.x + 6} cy={center.y} r="34" fill="none" stroke="#f472b6" strokeWidth="5" opacity="0.58" filter={`url(#${filterId})`} />
      <circle cx={center.x} cy={center.y} r="22" fill="#f8fafc" opacity="0.08" />
      {[0, 1, 2, 3].map((index) => (
        <motion.path
          key={index}
          d={`M ${center.x - 45 - index * 2} ${center.y - 13 + index * 9} H ${center.x - 12 + index * 5} M ${center.x + 8 - index * 3} ${center.y - 11 + index * 9} H ${center.x + 49 + index * 2}`}
          stroke={index % 2 ? "#f472b6" : color}
          strokeWidth={index % 2 ? 3 : 5}
          strokeDasharray={index % 2 ? "8 5" : "13 7"}
          opacity={0.82 - index * 0.1}
          animate={reduceMotion ? undefined : { x: index % 2 ? [0, 8, -3, 0] : [0, -7, 4, 0] }}
          transition={reduceMotion ? undefined : { duration: 0.48 + index * 0.09, repeat: Infinity, ease: "linear" }}
        />
      ))}
      {fragments.map(([dx, dy, size, rotation], index) => (
        <motion.rect
          key={`${dx}:${dy}`}
          x={center.x + dx}
          y={center.y + dy}
          width={size}
          height={Math.max(4, size / 2)}
          fill={index % 3 === 0 ? "#f8fafc" : index % 2 === 0 ? "#22d3ee" : "#f472b6"}
          opacity="0.76"
          transform={`rotate(${rotation} ${center.x + dx} ${center.y + dy})`}
          initial={reduceMotion ? false : { opacity: 0, scale: 0 }}
          animate={{ opacity: reduceMotion ? 0.72 : [0, 0.92, 0.28], scale: [0.35, 1.15, 1] }}
          transition={{ duration: reduceMotion ? 0 : 0.58, delay: delay + 0.05 + index * 0.025 }}
        />
      ))}
      <path d={`M ${center.x - 18} ${center.y - 43} L ${center.x + 8} ${center.y - 11} L ${center.x - 4} ${center.y + 8} L ${center.x + 20} ${center.y + 42}`} fill="none" stroke="#ffffff" strokeWidth="2" opacity="0.75" filter={`url(#${filterId})`} />
    </motion.g>
  );
}

function ImpactGlyph({ attribute, center, reduceMotion, delay, filterId }: ImpactProps & { attribute: AttackAttribute }) {
  const color = ATTACK_ATTRIBUTE_THEMES[attribute].primary;
  const props = { center, color, reduceMotion, delay, filterId };
  switch (attribute) {
    case "斩": return <SlashImpact {...props} />;
    case "打": return <StrikeImpact {...props} />;
    case "射": return <ShotImpact {...props} />;
    case "特": return <SpecialImpact {...props} />;
    case "知": return <KnowledgeImpact {...props} />;
    default: return <UnknownImpact {...props} />;
  }
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

function CinematicTexture({
  attribute,
  source,
  target,
  index,
  reduceMotion,
}: {
  attribute: AttackAttribute;
  source: Point;
  target: Point;
  index: number;
  reduceMotion: boolean;
}) {
  const dx = target.x - source.x;
  const dy = target.y - source.y;
  const distance = Math.max(1, Math.hypot(dx, dy));
  const angle = Math.atan2(dy, dx) * (180 / Math.PI);
  // 材质图中发射点与命中点分别位于画幅约 12% / 86%，据此反推动态画幅。
  const width = distance / 0.74;
  const height = width * (314 / 836);
  const x = source.x - width * 0.12;
  const y = source.y - height * 0.5;
  const opacity = Math.max(0.58, 0.9 - index * 0.12);

  return (
    <motion.g
      transform={`rotate(${angle} ${source.x} ${source.y})`}
      initial={reduceMotion ? false : { opacity: 0 }}
      animate={{ opacity }}
      transition={{ duration: reduceMotion ? 0 : 0.34, delay: index * 0.055, ease: "easeOut" }}
      style={{ mixBlendMode: "screen" }}
    >
      <image
        href={ATTACK_ATTRIBUTE_TEXTURES[attribute]}
        x={x}
        y={y}
        width={width}
        height={height}
        preserveAspectRatio="none"
        opacity={reduceMotion ? 0.68 : 0.95}
      />
    </motion.g>
  );
}

/**
 * 电影级属性攻击视觉：每个子属性拥有独立的轨迹材质、运动粒子、空间辉光和命中结构。
 * 多属性卡会逐层组合全部子属性；所有坐标仍由战斗关系层提供，因此阻挡改向和响应式缩放保持有效。
 */
export default function AttributeAttackEffect({
  attributes,
  path,
  source,
  target,
  reduceMotion,
}: AttributeAttackEffectProps) {
  const reactId = useId().replace(/:/g, "");
  const softGlowId = `attribute-soft-glow-${reactId}`;
  const hotGlowId = `attribute-hot-glow-${reactId}`;
  const direction = Math.atan2(target.y - source.y, target.x - source.x) * (180 / Math.PI);

  return (
    <g data-attack-vfx="cinematic">
      <defs>
        <filter id={softGlowId} x="-100%" y="-100%" width="300%" height="300%" colorInterpolationFilters="sRGB">
          <feGaussianBlur stdDeviation="6" result="wideGlow" />
          <feMerge>
            <feMergeNode in="wideGlow" />
            <feMergeNode in="SourceGraphic" />
          </feMerge>
        </filter>
        <filter id={hotGlowId} x="-120%" y="-120%" width="340%" height="340%" colorInterpolationFilters="sRGB">
          <feGaussianBlur in="SourceGraphic" stdDeviation="2" result="tightGlow" />
          <feGaussianBlur in="SourceGraphic" stdDeviation="8" result="wideGlow" />
          <feMerge>
            <feMergeNode in="wideGlow" />
            <feMergeNode in="tightGlow" />
            <feMergeNode in="SourceGraphic" />
          </feMerge>
        </filter>
      </defs>

      {attributes.map((attribute, index) => (
        <CinematicTexture
          key={`texture:${attribute}`}
          attribute={attribute}
          source={source}
          target={target}
          index={index}
          reduceMotion={reduceMotion}
        />
      ))}

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

      <g transform={`rotate(${direction} ${target.x} ${target.y})`}>
        {attributes.map((attribute, index) => (
          <ImpactGlyph
            key={`impact:${attribute}`}
            attribute={attribute}
            center={target}
            color={ATTACK_ATTRIBUTE_THEMES[attribute].primary}
            reduceMotion={reduceMotion}
            delay={index * 0.075}
            filterId={hotGlowId}
          />
        ))}
      </g>

      <motion.circle
        cx={source.x}
        cy={source.y}
        fill="none"
        stroke={ATTACK_ATTRIBUTE_THEMES[attributes[0] ?? "?"].accent}
        strokeWidth="2"
        filter={`url(#${softGlowId})`}
        initial={reduceMotion ? false : { r: 8, opacity: 0.9 }}
        animate={{ r: reduceMotion ? 18 : 30, opacity: reduceMotion ? 0.5 : 0 }}
        transition={{ duration: reduceMotion ? 0 : 0.52, ease: "easeOut" }}
      />
    </g>
  );
}
