export const QQ_MIN_LENGTH = 5;
export const QQ_MAX_LENGTH = 12;
export const QQ_WHITELIST_MAX_BYTES = 256 * 1024;
export const QQ_WHITELIST_MAX_MEMBERS = 10_000;

/** @param {unknown} value @param {number=} index */
export function normalizeQq(value, index) {
  let candidate;
  if (typeof value === "string") {
    candidate = value.trim().normalize("NFKC");
  } else if (typeof value === "number" && Number.isSafeInteger(value) && value >= 0) {
    candidate = String(value);
  } else {
    throw new Error(`${index ? `第 ${index} 条` : ""}QQ 必须是字符串或安全整数。`);
  }
  if (!new RegExp(`^[0-9]{${QQ_MIN_LENGTH},${QQ_MAX_LENGTH}}$`).test(candidate)) {
    throw new Error(`${index ? `第 ${index} 条` : ""}QQ 必须是 ${QQ_MIN_LENGTH}–${QQ_MAX_LENGTH} 位纯数字。`);
  }
  return candidate;
}

/** @param {unknown} root @returns {unknown[]} */
function resolveMembers(root) {
  if (Array.isArray(root)) return root;
  if (!root || typeof root !== "object") {
    throw new Error("JSON 顶层必须是成员数组，或包含 members、data、list 数组。");
  }
  /** @type {Record<string, unknown>} */
  const record = root;
  for (const name of ["members", "data", "list"]) {
    const key = Object.keys(record).find((candidate) => candidate.toLowerCase() === name);
    if (!key) continue;
    if (!Array.isArray(record[key])) throw new Error(`字段 ${key} 必须是数组。`);
    return record[key];
  }
  throw new Error("JSON 对象缺少 members、data 或 list 成员数组。");
}

/** @param {unknown} item @param {number} index @returns {unknown} */
function resolveQq(item, index) {
  if (typeof item === "string" || typeof item === "number") return item;
  if (!item || typeof item !== "object" || Array.isArray(item)) {
    throw new Error(`第 ${index} 条成员不是 QQ 字符串、数字或对象。`);
  }
  /** @type {Record<string, unknown>} */
  const record = item;
  for (const name of ["qq", "uin", "user_id"]) {
    const key = Object.keys(record).find((candidate) => candidate.toLowerCase() === name);
    if (key) return record[key];
  }
  throw new Error(`第 ${index} 条成员对象缺少 qq、uin 或 user_id 字段。`);
}

/**
 * @param {string} json
 * @param {number=} maxBytes
 * @param {number=} maxMembers
 * @returns {{totalCount: number, uniqueCount: number, duplicateCount: number}}
 */
export function previewQqWhitelistJson(
  json,
  maxBytes = QQ_WHITELIST_MAX_BYTES,
  maxMembers = QQ_WHITELIST_MAX_MEMBERS,
) {
  if (new TextEncoder().encode(json).byteLength > maxBytes) {
    throw new Error(`JSON 文件不能超过 ${Math.floor(maxBytes / 1024)} KiB。`);
  }
  let root;
  try {
    root = JSON.parse(json);
  } catch {
    throw new Error("JSON 格式无效，请检查文件内容。");
  }
  const members = resolveMembers(root);
  if (members.length === 0) throw new Error("拒绝导入空白名单，以免意外锁定全部账号。");
  if (members.length > maxMembers) throw new Error(`群成员条目不能超过 ${maxMembers} 条。`);
  const unique = new Set();
  members.forEach((item, index) => unique.add(normalizeQq(resolveQq(item, index + 1), index + 1)));
  return {
    totalCount: members.length,
    uniqueCount: unique.size,
    duplicateCount: members.length - unique.size,
  };
}
