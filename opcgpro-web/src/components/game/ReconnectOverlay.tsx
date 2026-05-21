"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { motion, AnimatePresence } from "framer-motion";
import { useNetStore } from "@/store/netStore";

/**
 * ReconnectOverlay — 断线重连全屏遮罩
 * 对应重构方案 §5.8.6
 *
 * 状态机:
 *   reconnecting → 显示倒计时 + 旋转动画
 *   recovering    → 显示"正在恢复游戏状态..."
 *   failed        → 显示"连接失败"，提供返回大厅按钮
 */
export default function ReconnectOverlay() {
  const router = useRouter();
  const { connState, reconnectCountdown } = useNetStore();
  const [remaining, setRemaining] = useState(0);

  // 收到新倒计时时启动本地秒级递减
  useEffect(() => {
    if (connState !== "reconnecting") return;
    setRemaining(reconnectCountdown);
    const t = setInterval(() => setRemaining((n) => Math.max(0, n - 1)), 1000);
    return () => clearInterval(t);
  }, [reconnectCountdown, connState]);

  const visible = connState === "reconnecting" || connState === "recovering";

  return (
    <AnimatePresence>
      {visible && (
        <motion.div
          className="fixed inset-0 z-50 flex flex-col items-center justify-center bg-black/80 backdrop-blur-sm"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
        >
          {/* 旋转加载指示器 */}
          <div className="w-12 h-12 border-4 border-orange-400 border-t-transparent rounded-full animate-spin mb-4" />

          {connState === "reconnecting" && (
            <>
              <p className="text-white text-xl font-bold">连接已断开</p>
              <p className="text-gray-400 mt-2">
                {remaining > 0 ? `${remaining} 秒后重试...` : "正在重连..."}
              </p>
            </>
          )}

          {connState === "recovering" && (
            <p className="text-white text-xl font-bold">正在恢复游戏状态...</p>
          )}
        </motion.div>
      )}

      {connState === "failed" && (
        <motion.div
          className="fixed inset-0 z-50 flex flex-col items-center justify-center bg-black/90"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
        >
          <p className="text-red-400 text-2xl font-bold mb-4">连接失败</p>
          <p className="text-gray-400 mb-6">无法重新连接到服务器，游戏已结束</p>
          <button
            onClick={() => router.push("/home")}
            className="bg-orange-500 text-white px-6 py-2 rounded-lg hover:bg-orange-400 transition-colors"
          >
            返回大厅
          </button>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
