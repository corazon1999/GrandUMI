import type { InjectionKey } from "vue";

/**
 * 卡图懒加载共享 IntersectionObserver 契约。
 *
 * 由 SearchResultPanel 提供一个全局 observer，所有 CardGridItem 共用它做图片懒加载，
 * 避免「每张卡各建一个 observer」（宽筛选下上千个）的开销；同时比原生 loading=lazy 更可控
 * （原生 lazy 在 content-visibility 的嵌套滚动容器里不生效，会一次性加载全部卡图）。
 */
export interface CardImgIO {
  /** 观察元素；进入视口(含 rootMargin)时触发一次 cb 后自动取消观察。无 observer 时立即 cb。 */
  observe(el: Element, cb: () => void): void;
  /** 取消观察（组件卸载时调用）。 */
  unobserve(el: Element): void;
}

export const CARD_IMG_IO: InjectionKey<CardImgIO> = Symbol("cardImgIO");
