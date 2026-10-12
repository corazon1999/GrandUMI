import { DATA_VERSION } from "./dataVersion";

const ATTEMPT_TIMEOUT_MS = 5_000;
const PRODUCTION_HOSTS = new Set(["ygo.grand-umi.com", "direct.grand-umi.com"]);

/** 卡牌资料按内容版本缓存；首选请求连同响应体均超时后，切换直连再有限重试。 */
export async function fetchCardData<T>(path: string): Promise<T> {
  const versionedPath = `${path}${path.includes("?") ? "&" : "?"}v=${DATA_VERSION}`;
  const sources = [versionedPath];
  if (typeof window !== "undefined" && PRODUCTION_HOSTS.has(window.location.hostname)
      && window.location.hostname !== "direct.grand-umi.com") {
    sources.push(new URL(versionedPath, "https://direct.grand-umi.com").href);
  }
  sources.push(sources.at(-1)!);
  let lastError: unknown;
  for (const source of sources) {
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout> | undefined;
    try {
      return await Promise.race([
        (async () => {
          const response = await fetch(source, { signal: controller.signal });
          if (!response.ok) throw new Error(`卡牌资料加载失败：HTTP ${response.status}`);
          return await response.json() as T;
        })(),
        new Promise<never>((_, reject) => {
          timer = setTimeout(() => {
            controller.abort();
            reject(new Error("卡牌资料请求超时"));
          }, ATTEMPT_TIMEOUT_MS);
        }),
      ]);
    } catch (error) {
      lastError = error;
    } finally {
      clearTimeout(timer);
    }
  }
  throw lastError instanceof Error ? lastError : new Error("卡牌资料加载失败，请检查网络后重试");
}
