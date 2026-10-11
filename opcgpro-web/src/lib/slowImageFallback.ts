export const SLOW_IMAGE_FALLBACK_MS = 2_000;

/** 只为进入视口的未完成图片设置一次慢请求回退；离屏、换图和卸载均可取消。 */
export function watchSlowImage({
  image,
  source,
  onSlow,
  timeoutMs = SLOW_IMAGE_FALLBACK_MS,
  schedule = setTimeout,
  cancel = clearTimeout,
  createObserver = typeof IntersectionObserver === "undefined"
    ? undefined
    : (callback: IntersectionObserverCallback) => new IntersectionObserver(callback, { rootMargin: "300px" }),
}: {
  image: HTMLImageElement;
  source: string;
  onSlow: () => void;
  timeoutMs?: number;
  schedule?: typeof setTimeout;
  cancel?: typeof clearTimeout;
  createObserver?: (callback: IntersectionObserverCallback) => IntersectionObserver;
}): () => void {
  let stopped = false;
  let timer: ReturnType<typeof setTimeout> | null = null;
  let observer: IntersectionObserver | undefined;
  const stop = () => {
    stopped = true;
    if (timer !== null) cancel(timer);
    observer?.disconnect();
    image.removeEventListener("load", stop);
    image.removeEventListener("error", stop);
  };
  const start = () => {
    if (stopped || timer !== null || image.src !== source || (image.complete && image.naturalWidth > 0)) return;
    observer?.disconnect();
    timer = schedule(() => {
      const shouldFallback = !stopped && image.isConnected && image.src === source
        && !(image.complete && image.naturalWidth > 0);
      stop();
      if (shouldFallback) onSlow();
    }, timeoutMs);
  };
  if (image.src !== source || (image.complete && image.naturalWidth > 0)) return stop;
  image.addEventListener("load", stop);
  image.addEventListener("error", stop);
  if (createObserver) {
    observer = createObserver((entries) => {
      if (entries.some((entry) => entry.isIntersecting)) start();
    });
    observer.observe(image);
  } else {
    start();
  }
  return stop;
}
