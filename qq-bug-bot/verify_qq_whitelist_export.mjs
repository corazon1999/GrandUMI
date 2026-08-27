import { createHash } from "node:crypto";
import { readFile, stat } from "node:fs/promises";
import { resolve } from "node:path";
import { previewQqWhitelistJson } from "../opcgpro-web/src/lib/qqWhitelist.mjs";

const TARGET_GROUP_ID = "297542853";
const TARGET_GROUP_NAME = "GrandUMI测试群";
const EXPECTED_ACTIONS = [
  "get_group_info(no_cache=true)",
  "get_group_member_list(no_cache=true)",
  "get_group_info(no_cache=true)",
];

function reject(condition, message) {
  if (condition) throw new Error(message);
}

function requirePositiveInteger(value, label) {
  reject(!Number.isInteger(value) || value <= 0 || value > 10_000, `${label}无效。`);
  return value;
}

async function verify(fileName) {
  const fullPath = resolve(fileName);
  let fileInfo;
  let raw;
  try {
    fileInfo = await stat(fullPath);
    raw = await readFile(fullPath);
  } catch {
    throw new Error("无法读取待校验的白名单文件。");
  }
  reject(!fileInfo.isFile(), "校验目标不是普通文件。");
  const text = raw.toString("utf8");
  let root;
  try {
    root = JSON.parse(text);
  } catch {
    throw new Error("导出 JSON 格式无效。");
  }
  reject(!root || typeof root !== "object" || Array.isArray(root), "导出 JSON 顶层格式无效。");

  const source = root.source;
  const validation = root.validation;
  reject(!source || typeof source !== "object" || Array.isArray(source), "导出来源元数据缺失。");
  reject(!validation || typeof validation !== "object" || Array.isArray(validation), "导出校验元数据缺失。");
  reject(source.protocol !== "OneBot 11", "导出协议标识无效。");
  reject(source.group_id !== TARGET_GROUP_ID, "导出群号不是固定测试群。");
  reject(source.group_name !== TARGET_GROUP_NAME, "导出群名不是固定测试群。");
  reject(JSON.stringify(source.actions) !== JSON.stringify(EXPECTED_ACTIONS), "实时 API 调用顺序无效。");
  reject(
    typeof source.fetched_at !== "string"
      || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}\+08:00$/.test(source.fetched_at)
      || !Number.isFinite(Date.parse(source.fetched_at)),
    "实时拉取时间无效。",
  );
  requirePositiveInteger(source.stability_attempt, "稳定性尝试次数");
  reject(source.stability_attempt > 3, "稳定性尝试次数超出有限重试上限。");

  const preview = previewQqWhitelistJson(text);
  const beforeCount = requirePositiveInteger(source.group_info_count_before, "前置群人数");
  const rawCount = requirePositiveInteger(source.api_raw_count, "成员列表人数");
  const afterCount = requirePositiveInteger(source.group_info_count_after, "后置群人数");
  reject(beforeCount !== rawCount || rawCount !== afterCount, "三段实时人数不完全一致。");
  reject(preview.totalCount !== rawCount || preview.uniqueCount !== rawCount, "成员列表人数与元数据不一致。");
  reject(preview.duplicateCount !== 0, "成员列表包含重复 QQ。");
  reject(!Array.isArray(root.members), "成员列表缺失。");
  reject(root.members.some((qq) => typeof qq !== "string" || !/^[0-9]{5,12}$/.test(qq)), "成员列表包含无效 QQ。");

  reject(validation.original_count !== rawCount, "原始成员人数元数据不一致。");
  reject(validation.unique_count !== rawCount, "唯一成员人数元数据不一致。");
  reject(validation.duplicate_count !== 0, "重复成员校验结果无效。");
  reject(validation.invalid_count !== 0, "无效成员校验结果无效。");
  reject(validation.cross_group_count !== 0, "串群校验结果无效。");
  reject(JSON.stringify(validation.group_ids_seen) !== JSON.stringify([TARGET_GROUP_ID]), "成员所属群元数据无效。");

  return {
    filePath: fullPath,
    memberCount: rawCount,
    fetchedAt: source.fetched_at,
    sha256: createHash("sha256").update(raw).digest("hex"),
  };
}

try {
  reject(process.argv.length !== 3, "必须且只能提供一个待校验 JSON 文件路径。");
  const result = await verify(process.argv[2]);
  process.stdout.write(`${JSON.stringify(result)}\n`);
} catch (error) {
  const message = error instanceof Error ? error.message : "未知错误";
  process.stderr.write(`本地白名单校验失败：${message}\n`);
  process.exitCode = 1;
}
