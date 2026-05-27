"use client";

import { useEffect, useState } from "react";

export function useResponsive() {
  const [size, setSize] = useState<"sm" | "md" | "lg">("md");

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

  return {
    size,
    cardSize: size === "sm" ? ("sm" as const) : size === "md" ? ("md" as const) : ("lg" as const),
    isMobile: size === "sm",
  };
}
