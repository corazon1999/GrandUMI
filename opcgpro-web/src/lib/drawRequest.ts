export const DRAW_REQUEST_DESCRIPTION_MAX_LENGTH = 500;

export type PreparedDrawRequestDescription =
  | { ok: true; description: string }
  | { ok: false; error: string };

/**
 * 生成发给服务端的 Bug 描述。这里只提供即时反馈；服务端仍会独立执行同样的权威校验。
 */
export function prepareDrawRequestDescription(rawDescription: string): PreparedDrawRequestDescription {
  const description = rawDescription.trim();
  if (!description) {
    return { ok: false, error: "请填写发生了什么 Bug" };
  }
  if (description.length > DRAW_REQUEST_DESCRIPTION_MAX_LENGTH) {
    return {
      ok: false,
      error: `Bug 描述不能超过 ${DRAW_REQUEST_DESCRIPTION_MAX_LENGTH} 个字符`,
    };
  }
  return { ok: true, description };
}
