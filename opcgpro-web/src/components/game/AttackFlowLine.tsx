"use client";

import { useEffect, useRef, useState, type RefObject } from "react";
import { useGameStore } from "@/store/gameStore";

const STAGE_W = 1280;
const STAGE_H = 720;

interface Point {
  x: number;
  y: number;
}

interface FlowPath {
  d: string;
  start: Point;
  end: Point;
}

interface Props {
  stageRef: RefObject<HTMLDivElement | null>;
}

/**
 * 战斗中的攻击流向线。
 *
 * 卡牌仍由各自区域负责布局；本层只读取带 data-battle-card-id 的节点位置，
 * 换算到固定 1280×720 设计画布坐标后绘制 SVG，不参与点击和牌桌布局。
 */
export default function AttackFlowLine({ stageRef }: Props) {
  const battle = useGameStore((s) => s.battle);
  const currentTurn = useGameStore((s) => s.currentTurn);
  const my = useGameStore((s) => s.my);
  const opponent = useGameStore((s) => s.opponent);
  const svgRef = useRef<SVGSVGElement>(null);
  const [flowPath, setFlowPath] = useState<FlowPath | null>(null);

  useEffect(() => {
    const stage = stageRef.current;
    const svg = svgRef.current;
    const defendingPlayer = currentTurn ? opponent : my;
    const targetId = battle?.targetIsLeader ? defendingPlayer?.leaderId : battle?.targetCardId;

    if (!stage || !svg || !battle?.attackerCardId || !targetId) {
      setFlowPath(null);
      return;
    }

    const findCardNode = (cardId: string) =>
      Array.from(stage.querySelectorAll<HTMLElement>("[data-battle-card-id]")).find(
        (node) => node.dataset.battleCardId === cardId,
      );

    const attackerNode = findCardNode(battle.attackerCardId);
    const targetNode = findCardNode(targetId);
    if (!attackerNode || !targetNode) {
      setFlowPath(null);
      return;
    }

    let frame = 0;
    const measure = () => {
      const stageRect = stage.getBoundingClientRect();
      const attackerRect = attackerNode.getBoundingClientRect();
      const targetRect = targetNode.getBoundingClientRect();
      if (stageRect.width <= 0 || stageRect.height <= 0) return;

      const scaleX = STAGE_W / stageRect.width;
      const scaleY = STAGE_H / stageRect.height;
      const center = (rect: DOMRect): Point => ({
        x: (rect.left + rect.width / 2 - stageRect.left) * scaleX,
        y: (rect.top + rect.height / 2 - stageRect.top) * scaleY,
      });

      const attacker = center(attackerRect);
      const target = center(targetRect);
      const directionY = target.y >= attacker.y ? 1 : -1;
      const start: Point = {
        x: attacker.x,
        y: attacker.y + directionY * (attackerRect.height * scaleY / 2 + 8),
      };
      const end: Point = {
        x: target.x,
        y: target.y - directionY * (targetRect.height * scaleY / 2 + 14),
      };
      const bend = Math.max(42, Math.abs(end.y - start.y) * 0.4);
      const d = [
        `M ${start.x.toFixed(1)} ${start.y.toFixed(1)}`,
        `C ${start.x.toFixed(1)} ${(start.y + directionY * bend).toFixed(1)}`,
        `${end.x.toFixed(1)} ${(end.y - directionY * bend).toFixed(1)}`,
        `${end.x.toFixed(1)} ${end.y.toFixed(1)}`,
      ].join(" ");

      setFlowPath({ d, start, end });
    };
    const scheduleMeasure = () => {
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(measure);
    };

    measure();
    const observer = new ResizeObserver(scheduleMeasure);
    observer.observe(stage);
    observer.observe(attackerNode);
    observer.observe(targetNode);
    window.addEventListener("resize", scheduleMeasure);

    return () => {
      cancelAnimationFrame(frame);
      observer.disconnect();
      window.removeEventListener("resize", scheduleMeasure);
    };
  }, [battle, currentTurn, my, opponent, stageRef]);

  return (
    <svg
      ref={svgRef}
      viewBox={`0 0 ${STAGE_W} ${STAGE_H}`}
      className="pointer-events-none absolute inset-0 z-30 h-full w-full overflow-visible"
      aria-hidden="true"
    >
      <defs>
        <linearGradient
          id="attack-flow-gradient"
          gradientUnits="userSpaceOnUse"
          x1={flowPath?.start.x ?? 0}
          y1={flowPath?.start.y ?? STAGE_H}
          x2={flowPath?.end.x ?? 0}
          y2={flowPath?.end.y ?? 0}
        >
          <stop offset="0%" stopColor="#f59e0b" stopOpacity="0.55" />
          <stop offset="55%" stopColor="#facc15" stopOpacity="0.95" />
          <stop offset="100%" stopColor="#fef08a" />
        </linearGradient>
        <filter id="attack-flow-glow" x="-60%" y="-60%" width="220%" height="220%">
          <feGaussianBlur stdDeviation="3.4" result="blur" />
          <feMerge>
            <feMergeNode in="blur" />
            <feMergeNode in="SourceGraphic" />
          </feMerge>
        </filter>
        <marker
          id="attack-flow-arrowhead"
          viewBox="0 0 14 14"
          refX="12"
          refY="7"
          markerWidth="14"
          markerHeight="14"
          orient="auto"
          markerUnits="userSpaceOnUse"
        >
          <path d="M 1 1 L 13 7 L 1 13 L 4 7 Z" fill="#fde047" stroke="#fef9c3" strokeWidth="1" />
        </marker>
      </defs>

      {flowPath && (
        <g filter="url(#attack-flow-glow)">
          <path
            d={flowPath.d}
            fill="none"
            stroke="#f59e0b"
            strokeWidth="11"
            strokeLinecap="round"
            opacity="0.18"
          />
          <path
            d={flowPath.d}
            fill="none"
            stroke="url(#attack-flow-gradient)"
            strokeWidth="4.5"
            strokeLinecap="round"
            markerEnd="url(#attack-flow-arrowhead)"
            opacity="0.88"
          />
          <path
            d={flowPath.d}
            className="attack-flow-stream"
            fill="none"
            stroke="#fff7ae"
            strokeWidth="2.6"
            strokeLinecap="round"
            strokeDasharray="3 18"
          />
        </g>
      )}
    </svg>
  );
}
