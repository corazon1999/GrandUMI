const LOCAL_WS_URL = "ws://localhost:8080/ws";
const PRIMARY_HOST = "grand-umi.com";
const DIRECT_HOST = "direct.grand-umi.com";
const RUNTIME_CONFIG_PATH = "/network-endpoints.json";
const RUNTIME_CACHE_KEY = "grandumi_network_endpoints_v1";
const RUNTIME_CACHE_TTL_MS = 10 * 60 * 1000;

interface RuntimeEndpointConfig {
  version?: number;
  hosts?: string[];
  endpoints?: Array<string | { url?: string; enabled?: boolean }>;
}

interface CachedRuntimeConfig {
  savedAt: number;
  config: RuntimeEndpointConfig;
}

export function buildWebSocketEndpoints(
  configuredUrl: string,
  hostname?: string,
  pageProtocol = "https:",
): string[] {
  if (hostname !== PRIMARY_HOST && hostname !== DIRECT_HOST) return [configuredUrl];

  const socketProtocol = pageProtocol === "http:" ? "ws" : "wss";
  const directUrl = `${socketProtocol}://${DIRECT_HOST}/ws`;
  // 正式服优先香港直连，主域的代理入口作为跨线路兜底。
  return uniqueEndpoints([directUrl, configuredUrl]);
}

/**
 * 立即返回可用配置：优先使用十分钟内的运行时缓存，确保页面首屏不被配置请求阻塞。
 */
export function getWebSocketEndpoints(): string[] {
  const defaults = getDefaultEndpoints();
  if (typeof window === "undefined") return defaults;

  const cached = readCachedConfig();
  return cached ? resolveRuntimeConfig(cached.config, defaults) : defaults;
}

/**
 * 从同源 JSON 刷新线路清单。失败时继续使用缓存或构建配置，不中断登录。
 * 该文件可在部署时单独替换，因此切换入口不需要重新构建前端。
 */
export async function refreshWebSocketEndpoints(): Promise<string[]> {
  const defaults = getDefaultEndpoints();
  if (typeof window === "undefined") return defaults;

  const controller = new AbortController();
  const timeout = window.setTimeout(() => controller.abort(), 2_500);
  try {
    const response = await fetch(`${RUNTIME_CONFIG_PATH}?t=${Date.now()}`, {
      cache: "no-store",
      signal: controller.signal,
      headers: { accept: "application/json" },
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const config = await response.json() as RuntimeEndpointConfig;
    const endpoints = resolveRuntimeConfig(config, defaults);
    if (endpoints.length === 0) throw new Error("线路清单为空");
    localStorage.setItem(RUNTIME_CACHE_KEY, JSON.stringify({ savedAt: Date.now(), config }));
    return endpoints;
  } catch (error) {
    console.info("[NetManager] 运行时线路清单不可用，继续使用本地配置", error);
    return getWebSocketEndpoints();
  } finally {
    window.clearTimeout(timeout);
  }
}

function getDefaultEndpoints(): string[] {
  const configuredUrl = process.env.NEXT_PUBLIC_WS_URL ?? LOCAL_WS_URL;
  if (typeof window === "undefined") return [configuredUrl];
  return buildWebSocketEndpoints(configuredUrl, window.location.hostname, window.location.protocol);
}

function readCachedConfig(): CachedRuntimeConfig | null {
  try {
    const parsed = JSON.parse(localStorage.getItem(RUNTIME_CACHE_KEY) ?? "null") as CachedRuntimeConfig | null;
    if (!parsed || Date.now() - parsed.savedAt > RUNTIME_CACHE_TTL_MS) return null;
    return parsed;
  } catch {
    return null;
  }
}

function resolveRuntimeConfig(config: RuntimeEndpointConfig, defaults: string[]): string[] {
  if (!Array.isArray(config.endpoints)) return defaults;
  if (Array.isArray(config.hosts)
      && !config.hosts.includes("*")
      && !config.hosts.includes(window.location.hostname)) return defaults;

  const endpoints = config.endpoints.flatMap((item) => {
    if (typeof item === "string") return [item];
    return item.enabled === false || !item.url ? [] : [item.url];
  }).filter(isSafeWebSocketUrl);
  return endpoints.length > 0 ? uniqueEndpoints(endpoints) : defaults;
}

function isSafeWebSocketUrl(raw: string): boolean {
  try {
    const url = new URL(raw);
    if (url.protocol !== "ws:" && url.protocol !== "wss:") return false;
    return window.location.protocol !== "https:" || url.protocol === "wss:";
  } catch {
    return false;
  }
}

function uniqueEndpoints(endpoints: string[]): string[] {
  return [...new Set(endpoints.filter(Boolean))];
}
