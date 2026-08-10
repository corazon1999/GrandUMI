"use client";

import { useEffect, useRef, useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { useGameAnimation } from "@/hooks/useGameAnimation";
import { useGameStore } from "@/store/gameStore";

type TurnBanner = {
  id: number;
  kind: "turn";
  side: "my" | "opponent";
  text: string;
  turnCount: number;
};

type ResultBanner = {
  id: number;
  kind: "result";
  text: string;
  color: string;
};

type Banner = TurnBanner | ResultBanner;

/**
 * AnimationLayer — 根据服务端 lastAction 驱动战斗动画特效
 * 无交互的纯视觉层，渲染在游戏界面上方
 *
 * 动画类型：
 *   - damage：屏幕震动 + 红色闪光
 *   - koUnit：目标卡牌爆炸粒子
 *   - turnStart：回合切换横幅
 *   - gameOver：胜负结果横幅
 */
export default function AnimationLayer() {
  const anim = useGameAnimation();
  const mode = useGameStore((state) => state.mode);

  const [shake, setShake] = useState(false);
  const [flash, setFlash] = useState(false);
  const [banner, setBanner] = useState<Banner | null>(null);
  const bannerIdRef = useRef(0);
  const bannerTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => () => {
    if (bannerTimerRef.current) clearTimeout(bannerTimerRef.current);
  }, []);

  useEffect(() => {
    switch (anim.type) {
      case "damage":
        setFlash(true);
        setShake(true);
        setTimeout(() => setFlash(false), 200);
        setTimeout(() => setShake(false), 500);
        break;

      case "koUnit":
        setFlash(true);
        setTimeout(() => setFlash(false), 150);
        break;

      case "turnStart":
        // 观战快照没有“我方”视角，避免把所有回合都误报成“对手回合”。
        if (mode === "Observer") break;
        if (bannerTimerRef.current) clearTimeout(bannerTimerRef.current);
        bannerIdRef.current += 1;
        const turnBanner: TurnBanner = {
          id: bannerIdRef.current,
          kind: "turn",
          side: anim.side,
          text: anim.side === "my" ? "我的回合！" : "对手回合",
          turnCount: anim.turnCount,
        };
        setBanner(turnBanner);
        bannerTimerRef.current = setTimeout(() => {
          setBanner((current) => current?.id === turnBanner.id ? null : current);
          bannerTimerRef.current = null;
        }, 2200);
        break;

      case "gameOver":
        if (bannerTimerRef.current) clearTimeout(bannerTimerRef.current);
        bannerTimerRef.current = null;
        bannerIdRef.current += 1;
        setBanner({
          id: bannerIdRef.current,
          kind: "result",
          text: anim.isWin ? "胜利！" : "失败",
          color: anim.isWin ? "bg-yellow-500" : "bg-red-600",
        });
        // gameOver 横幅不自动消失
        break;

      default:
        break;
    }
  }, [anim, mode]);

  return (
    <>
      {/* 屏幕震动 */}
      <motion.div
        className="fixed inset-0 pointer-events-none z-20"
        animate={shake ? { x: [0, -6, 6, -4, 4, 0], y: [0, 3, -3, 2, -2, 0] } : {}}
        transition={{ duration: 0.4 }}
      />

      {/* 红色闪光（伤害/KO） */}
      <AnimatePresence>
        {flash && (
          <motion.div
            className="fixed inset-0 pointer-events-none z-20 bg-red-500/20"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.15 }}
          />
        )}
      </AnimatePresence>

      {/* 横幅提示（回合开始/游戏结束）：只做视觉提示，不拦截牌桌操作 */}
      <AnimatePresence mode="wait">
        {banner && (
          <motion.div
            key={banner.id}
            className="pointer-events-none fixed inset-0 z-30 flex items-center justify-center"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.18 }}
            role="status"
            aria-live="assertive"
          >
            {banner.kind === "turn" ? (
              <>
                <motion.div
                  className="absolute inset-0 bg-black/25"
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  exit={{ opacity: 0 }}
                />
                <motion.div
                  className={[
                    "absolute inset-x-0 h-px bg-gradient-to-r from-transparent to-transparent",
                    banner.side === "my" ? "via-amber-200/90" : "via-sky-200/90",
                  ].join(" ")}
                  initial={{ scaleX: 0, opacity: 0 }}
                  animate={{ scaleX: 1, opacity: 1 }}
                  exit={{ scaleX: 0.35, opacity: 0 }}
                  transition={{ duration: 0.42, ease: "easeOut" }}
                />
                <motion.div
                  className={[
                    "relative w-[34rem] max-w-[calc(100%-2rem)] overflow-hidden rounded-2xl border px-10 py-5 text-center text-white backdrop-blur-md",
                    banner.side === "my"
                      ? "border-amber-200/70 bg-gradient-to-r from-amber-950/95 via-orange-500/90 to-amber-950/95 shadow-[0_0_64px_rgba(251,146,60,0.55)]"
                      : "border-sky-200/70 bg-gradient-to-r from-blue-950/95 via-blue-600/90 to-blue-950/95 shadow-[0_0_64px_rgba(56,189,248,0.5)]",
                  ].join(" ")}
                  initial={{
                    opacity: 0,
                    scale: 0.82,
                    x: banner.side === "my" ? -120 : 120,
                  }}
                  animate={{ opacity: 1, scale: 1, x: 0 }}
                  exit={{
                    opacity: 0,
                    scale: 1.06,
                    x: banner.side === "my" ? 80 : -80,
                  }}
                  transition={{ type: "spring", stiffness: 230, damping: 22 }}
                >
                  <div
                    className={[
                      "absolute inset-x-10 top-0 h-px",
                      banner.side === "my" ? "bg-amber-100" : "bg-sky-100",
                    ].join(" ")}
                  />
                  <p className="text-xs font-black tracking-[0.35em] text-white/75">
                    第 {banner.turnCount} 回合
                  </p>
                  <p className="mt-1 text-5xl font-black tracking-[0.12em] drop-shadow-[0_2px_8px_rgba(0,0,0,0.65)]">
                    {banner.text}
                  </p>
                  {banner.side === "my" && (
                    <p className="mt-1 text-sm font-bold tracking-[0.22em] text-amber-50/90">该你了</p>
                  )}
                </motion.div>
              </>
            ) : (
              <motion.div
                className={`${banner.color} rounded-xl px-8 py-3 text-2xl font-bold text-white shadow-2xl`}
                initial={{ opacity: 0, scale: 0.5, y: -20 }}
                animate={{ opacity: 1, scale: 1, y: 0 }}
                exit={{ opacity: 0, scale: 0.8, y: -10 }}
                transition={{ type: "spring", stiffness: 200, damping: 20 }}
              >
                {banner.text}
              </motion.div>
            )}
          </motion.div>
        )}
      </AnimatePresence>
    </>
  );
}
