export const SESSION_REPLACED_CLOSE_CODE = 4009;
export const SESSION_REPLACED_NOTICE_KEY = "grandumi_session_replaced_notice";
export const DEFAULT_SESSION_REPLACED_NOTICE = "账号已在其他地方登录，请重新登录。";
const CLIENT_INSTANCE_ID_KEY = "grandumi_client_instance_id";

export function getClientInstanceId(): string | undefined {
  if (typeof window === "undefined") return undefined;
  let clientInstanceId = sessionStorage.getItem(CLIENT_INSTANCE_ID_KEY);
  if (!clientInstanceId) {
    clientInstanceId = typeof crypto.randomUUID === "function"
      ? crypto.randomUUID()
      : `${Date.now()}-${Math.random().toString(36).slice(2)}`;
    sessionStorage.setItem(CLIENT_INSTANCE_ID_KEY, clientInstanceId);
  }
  return clientInstanceId;
}

export function getSessionReplacedNotice(): string | null {
  if (typeof window === "undefined") return null;
  return sessionStorage.getItem(SESSION_REPLACED_NOTICE_KEY);
}

export function rememberSessionReplacedNotice(reason?: string | null): string {
  const notice = reason?.trim() || DEFAULT_SESSION_REPLACED_NOTICE;
  if (typeof window !== "undefined") {
    sessionStorage.setItem(SESSION_REPLACED_NOTICE_KEY, notice);
  }
  return notice;
}

export function clearSessionReplacedNotice() {
  if (typeof window !== "undefined") {
    sessionStorage.removeItem(SESSION_REPLACED_NOTICE_KEY);
  }
}
