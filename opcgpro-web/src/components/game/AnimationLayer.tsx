"use client";

import { useEffect, useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { useGameAnimation, type AnimationEvent } from "@/hooks/useGameAnimation";

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

  const [shake, setShake] = useState(false);
  const [flash, setFlash] = useState(false);
  const [banner, setBanner] = useState<{ text: string; color: string } | null>(null);

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
        setBanner({
          text: anim.side === "my" ? "我的回合！" : "对手回合",
          color: anim.side === "my" ? "bg-orange-500" : "bg-blue-500",
        });
        setTimeout(() => setBanner(null), 2000);
        break;

      case "gameOver":
        setBanner({
          text: anim.isWin ? "胜利！" : "失败",
          color: anim.isWin ? "bg-yellow-500" : "bg-red-600",
        });
        // gameOver 横幅不自动消失
        break;

      default:
        break;
    }
  }, [anim]);

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

      {/* 横幅提示（回合开始/游戏结束） */}
      <AnimatePresence>
        {banner && (
          <motion.div
            className="fixed top-1/4 left-1/2 -translate-x-1/2 -translate-y-1/2 z-30 pointer-events-none"
            initial={{ opacity: 0, scale: 0.5, y: -20 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.8, y: -10 }}
            transition={{ type: "spring", stiffness: 200, damping: 20 }}
          >
            <div className={`${banner.color} text-white px-8 py-3 rounded-xl text-2xl font-bold shadow-2xl`}>
              {banner.text}
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </>
  );
}
