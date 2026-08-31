import { CLIENT_VERSION, type NetworkDiagnostics } from "@/net/NetManager";
import type { ClientFeedbackEvidenceV1, FeedbackDisconnectCategory } from "@/types/net";

export const CLIENT_BUILD_COMMIT = process.env.NEXT_PUBLIC_GRANDUMI_COMMIT?.trim() || "unknown";

function finiteOrNull(value: number | null): number | null {
  return value !== null && Number.isFinite(value) ? Math.max(0, Math.round(value)) : null;
}

function classifyDisconnectReason(reason: string | null): FeedbackDisconnectCategory {
  if (!reason) return "unknown";
  const code = /^WebSocket ([0-9]{3,5})$/i.exec(reason)?.[1];
  if (code === "1000") return "normal";
  if (code === "1001") return "going_away";
  if (code === "1006") return "abnormal";
  if (code === "4009") return "session_replaced";
  if (code) return "websocket_error";

  const normalized = reason.toLowerCase();
  if (normalized.includes("其他地方登录") || normalized.includes("异地登录") || normalized.includes("session replaced")) {
    return "session_replaced";
  }
  if (normalized.includes("timeout") || normalized.includes("超时")) return "timeout";
  if (normalized.includes("维护")) return "maintenance";
  if (normalized.includes("白名单") || normalized.includes("准入") || normalized.includes("access revoked")) {
    return "access_revoked";
  }
  if (normalized.includes("network") || normalized.includes("网络") || normalized.includes("offline")) return "network";
  return "other";
}

/**
 * 仅构建排障所需的客户端非权威证据。严禁在此加入账号、昵称、聊天、URL、牌面或 gameStore。
 */
export function buildClientFeedbackEvidence(
  context: "lobby" | "game",
  connectionState: string,
  diagnostics: NetworkDiagnostics,
): ClientFeedbackEvidenceV1 {
  const hasWindow = typeof window !== "undefined";
  const width = hasWindow ? Math.max(0, Math.round(window.innerWidth)) : 0;
  const height = hasWindow ? Math.max(0, Math.round(window.innerHeight)) : 0;
  const standalone = hasWindow
    && (window.matchMedia("(display-mode: standalone), (display-mode: fullscreen)").matches
      || Boolean((navigator as Navigator & { standalone?: boolean }).standalone));

  return {
    schema: "grandumi.feedback.client.v1",
    capturedAtUtc: new Date().toISOString(),
    client: {
      version: CLIENT_VERSION,
      commit: CLIENT_BUILD_COMMIT,
      context,
    },
    connection: {
      state: connectionState,
      endpointHost: diagnostics.endpointHost.slice(0, 160),
      connectionGeneration: Math.max(0, Math.trunc(diagnostics.connectionGeneration)),
      reconnectCount: Math.max(0, Math.trunc(diagnostics.reconnectCount)),
      endpointFailureCount: Math.max(0, Math.trunc(diagnostics.endpointFailureCount)),
      handshakeMs: finiteOrNull(diagnostics.handshakeMs),
      rttMs: finiteOrNull(diagnostics.rttMs),
      rttP95Ms: finiteOrNull(diagnostics.rttP95Ms),
      actionRoundTripMs: finiteOrNull(diagnostics.actionRoundTripMs),
      actionRoundTripP95Ms: finiteOrNull(diagnostics.actionRoundTripP95Ms),
      disconnectCategory: classifyDisconnectReason(diagnostics.lastDisconnectReason),
      stateDeltaEnabled: diagnostics.stateDeltaEnabled,
      stateDeltaCount: Math.max(0, Math.trunc(diagnostics.stateDeltaCount)),
      fullStateCount: Math.max(0, Math.trunc(diagnostics.fullStateCount)),
      maxMessageQueueDepth: Math.max(0, Math.trunc(diagnostics.maxMessageQueueDepth)),
    },
    viewport: {
      width,
      height,
      orientation: width > height ? "landscape" : "portrait",
      devicePixelRatio: hasWindow && Number.isFinite(window.devicePixelRatio)
        ? Math.max(0, Math.min(8, window.devicePixelRatio))
        : 1,
      standalone,
      online: typeof navigator === "undefined" ? true : navigator.onLine,
    },
  };
}
