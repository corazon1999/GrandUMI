const LOCAL_WS_URL = "ws://localhost:8080/ws";
const PRIMARY_HOST = "grand-umi.com";
const DIRECT_HOST = "direct.grand-umi.com";

export function buildWebSocketEndpoints(
  configuredUrl: string,
  hostname?: string,
  pageProtocol = "https:",
): string[] {
  if (hostname !== PRIMARY_HOST && hostname !== DIRECT_HOST) return [configuredUrl];

  const socketProtocol = pageProtocol === "http:" ? "ws" : "wss";
  const directUrl = `${socketProtocol}://${DIRECT_HOST}/ws`;
  // 正式服直连的稳态 RTT 显著低于 Cloudflare WebSocket；主域也优先直连，
  // Cloudflare 继续作为源站直连不可用时的跨地域备用入口。
  const ordered = [directUrl, configuredUrl];

  return [...new Set(ordered)];
}

export function getWebSocketEndpoints(): string[] {
  const configuredUrl = process.env.NEXT_PUBLIC_WS_URL ?? LOCAL_WS_URL;
  if (typeof window === "undefined") return [configuredUrl];
  return buildWebSocketEndpoints(configuredUrl, window.location.hostname, window.location.protocol);
}
