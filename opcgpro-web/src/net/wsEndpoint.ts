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
  const ordered = hostname === DIRECT_HOST
    ? [directUrl, configuredUrl]
    : [configuredUrl, directUrl];

  return [...new Set(ordered)];
}

export function getWebSocketEndpoints(): string[] {
  const configuredUrl = process.env.NEXT_PUBLIC_WS_URL ?? LOCAL_WS_URL;
  if (typeof window === "undefined") return [configuredUrl];
  return buildWebSocketEndpoints(configuredUrl, window.location.hostname, window.location.protocol);
}
