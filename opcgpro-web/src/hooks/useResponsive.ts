"use client";

import { createContext, useContext, useEffect, useState } from "react";

export type CardSize = "sm" | "md" | "lg";

/**
 * 固定卡牌尺寸档覆盖。
 * 当处于固定设计画布（如对战页 scale-to-fit）时，用此 Context 强制画布内卡牌尺寸档，
 * 使画布内组件不随真实视口矮小而缩到更小档（画布已整体等比缩放）。
 * null = 不覆盖，按真实视口判断。
 */
export const CardSizeOverride = createContext<CardSize | null>(null);

export function useResponsive() {
  const override = useContext(CardSizeOverride);
  const [size, setSize] = useState<CardSize>("md");

  useEffect(() => {
    function update() {
      const w = window.innerWidth;
      const h = window.innerHeight;
      if (w < 1100 || h < 780) setSize("sm");
      else if (w < 1536 || h < 940) setSize("md");
      else setSize("lg");
    }

    update();
    window.addEventListener("resize", update);
    return () => window.removeEventListener("resize", update);
  }, []);

  const effective: CardSize = override ?? size;

  return {
    size: effective,
    cardSize: effective,
    isMobile: effective === "sm",
  };
}
