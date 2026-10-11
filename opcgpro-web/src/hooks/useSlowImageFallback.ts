"use client";

import { useEffect, useRef, type RefObject } from "react";
import { directAssetSrc } from "@/lib/sprite";
import { watchSlowImage } from "@/lib/slowImageFallback";

/** CDN 慢请求只尝试一次对应直连图，不提前下载离屏图片，也不重试已使用的直连。 */
export function useSlowImageFallback(
  imageRef: RefObject<HTMLImageElement | null>,
  source: string,
  onFallback: (directSource: string) => void,
  enabled = true,
): void {
  const onFallbackRef = useRef(onFallback);
  onFallbackRef.current = onFallback;
  useEffect(() => {
    const image = imageRef.current;
    if (!enabled || !image || !source) return;
    const directSource = directAssetSrc(source);
    const absoluteSource = new URL(source, document.baseURI).href;
    if (!directSource || directSource === absoluteSource) return;
    return watchSlowImage({ image, source: absoluteSource, onSlow: () => onFallbackRef.current(directSource) });
  }, [enabled, imageRef, source]);
}
