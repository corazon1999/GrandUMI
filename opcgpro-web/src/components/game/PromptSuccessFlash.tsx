"use client";

import { useEffect, useState } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { useGameStore } from "@/store/gameStore";

/**
 * #241 选择成功提示：目标/确认类 Prompt 点「确认」后弹一个居中的「选择成功 ✓」瞬时浮层。
 * 由 gameStore.promptFlash（nonce 递增）触发，约 1.1s 后自动淡出。pointer-events-none 不挡操作。
 * 与 PromptOverlay 同挂在对战页（仅玩家视角），因确认后 PromptOverlay 会随快照关闭，故用全局 nonce 而非弹窗内本地态。
 */
export default function PromptSuccessFlash() {
  const flash = useGameStore((s) => s.promptFlash);
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    if (flash === 0) return; // 初始不弹
    setVisible(true);
    const t = setTimeout(() => setVisible(false), 1100);
    return () => clearTimeout(t);
  }, [flash]);

  return (
    <AnimatePresence>
      {visible && (
        <motion.div
          key={flash}
          className="pointer-events-none fixed inset-0 z-[110] flex items-center justify-center"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.18 }}
        >
          <motion.div
            className="flex items-center gap-2 rounded-full bg-emerald-500/90 px-6 py-2.5 text-base font-black text-white shadow-2xl ring-1 ring-emerald-200/40"
            initial={{ scale: 0.8, y: 8 }}
            animate={{ scale: 1, y: 0 }}
            exit={{ scale: 0.9, opacity: 0 }}
            transition={{ type: "spring", stiffness: 320, damping: 22 }}
          >
            <span className="text-lg leading-none">✓</span>
            选择成功
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
