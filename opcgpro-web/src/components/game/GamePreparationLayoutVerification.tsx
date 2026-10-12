"use client";

import { useState } from "react";
import LayoutPreviewFrame from "@/components/home/LayoutPreviewFrame";
import GamePreparationOverlay from "./GamePreparationOverlay";

/** 验证实际准备提示在手机旋转画布上的显示与重试操作。 */
export default function GamePreparationLayoutVerification({ mobile, failed }: { mobile: boolean; failed: boolean }) {
  const [error, setError] = useState(failed ? "卡牌资料加载失败，请检查网络后重试。" : null);
  return <LayoutPreviewFrame mode={mobile ? "mobile-landscape" : "desktop"} rotateQuarterTurn={mobile} edgeToEdge>
    <main className="relative h-full w-full bg-[#07111f]">
      <GamePreparationOverlay error={error} onRetry={() => setError(null)} />
    </main>
  </LayoutPreviewFrame>;
}
