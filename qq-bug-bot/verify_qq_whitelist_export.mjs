import { createHash } from "node:crypto";
import { readFile, stat } from "node:fs/promises";
import { resolve } from "node:path";
import { previewQqWhitelistJson } from "../opcgpro-web/src/lib/qqWhitelist.mjs";

const TARGET_GROUPS = [
  { groupId: "297542853", expectedName: "UMI网咖" },
  { groupId: "524996856", expectedName: "UMI网咖2店" },
];
const TARGET_GROUP_IDS = TARGET_GROUPS.map(({ groupId }) => groupId);
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

function requireNonNegativeInteger(value, label) {
  reject(!Number.isInteger(value) || value < 0 || value > 20_000, `${label}无效。`);
  return value;
}

function requireQqList(value, label, { allowEmpty = true } = {}) {
  reject(!Array.isArray(value) || (!allowEmpty && value.length === 0), `${label}无效。`);
  reject(value.some((qq) => typeof qq !== "string" || !/^[0-9]{5,12}$/.test(qq)), `${label}包含无效 QQ。`);
  const normalized = [...new Set(value)].sort();
  reject(JSON.stringify(value) !== JSON.stringify(normalized), `${label}必须排序且不得重复。`);
  return value;
}

function requireGroupName(value, label) {
  reject(
    typeof value !== "string"
      || value.length < 1
      || value.length > 100
      || value !== value.normalize("NFKC").trim()
      || /[\u0000-\u001f\u007f-\u009f]/.test(value),
    `${label}无效。`,
  );
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
  reject(JSON.stringify(source.group_ids) !== JSON.stringify(TARGET_GROUP_IDS), "导出来源不是固定双群。 ");
  reject(JSON.stringify(source.actions) !== JSON.stringify(EXPECTED_ACTIONS), "实时 API 调用顺序无效。");
  reject(
    typeof source.fetched_at !== "string"
      || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}\+08:00$/.test(source.fetched_at)
      || !Number.isFinite(Date.parse(source.fetched_at)),
    "实时拉取时间无效。",
  );
  const configuredExcludedBots = requireQqList(source.excluded_bot_qqs, "机器人排除配置");
  const configuredExcludedBotSet = new Set(configuredExcludedBots);
  reject(!Array.isArray(source.groups) || source.groups.length !== TARGET_GROUPS.length, "双群来源元数据缺失。");

  let originalCount = 0;
  let eligibleCount = 0;
  let excludedBotCount = 0;
  const eligibleMembersAcrossGroups = [];
  const groupMemberCounts = [];
  for (let index = 0; index < TARGET_GROUPS.length; index += 1) {
    const expected = TARGET_GROUPS[index];
    const group = source.groups[index];
    reject(!group || typeof group !== "object" || Array.isArray(group), `第 ${index + 1} 个来源群元数据无效。`);
    reject(group.group_id !== expected.groupId, `第 ${index + 1} 个来源群号无效。`);
    const groupName = requireGroupName(group.group_name, `群 ${expected.groupId} 名称`);
    reject(groupName !== expected.expectedName, `群 ${expected.groupId} 名称不匹配。`);
    const stabilityAttempt = requirePositiveInteger(group.stability_attempt, `群 ${expected.groupId} 稳定性尝试次数`);
    reject(stabilityAttempt > 3, `群 ${expected.groupId} 稳定性尝试次数超出有限重试上限。`);
    const beforeCount = requirePositiveInteger(group.group_info_count_before, `群 ${expected.groupId} 前置人数`);
    const rawCount = requirePositiveInteger(group.api_raw_count, `群 ${expected.groupId} 成员列表人数`);
    const afterCount = requirePositiveInteger(group.group_info_count_after, `群 ${expected.groupId} 后置人数`);
    const groupEligibleCount = requirePositiveInteger(group.eligible_count, `群 ${expected.groupId} 排除机器人后人数`);
    const groupExcludedBotCount = requireNonNegativeInteger(group.excluded_bot_count, `群 ${expected.groupId} 机器人数量`);
    const groupExcludedBots = requireQqList(group.excluded_bot_qqs, `群 ${expected.groupId} 已排除机器人`);
    const groupEligibleMembers = requireQqList(group.members, `群 ${expected.groupId} 排除机器人后成员`, { allowEmpty: false });
    reject(beforeCount !== rawCount || rawCount !== afterCount, `群 ${expected.groupId} 三段实时人数不完全一致。`);
    reject(rawCount !== groupEligibleCount + groupExcludedBotCount, `群 ${expected.groupId} 人数分项不一致。`);
    reject(groupEligibleMembers.length !== groupEligibleCount, `群 ${expected.groupId} 成员明细与人数不一致。`);
    reject(groupExcludedBots.length !== groupExcludedBotCount, `群 ${expected.groupId} 机器人排除明细不一致。`);
    reject(groupExcludedBots.some((qq) => !configuredExcludedBotSet.has(qq)), `群 ${expected.groupId} 排除了未配置的 QQ。`);
    reject(groupEligibleMembers.some((qq) => configuredExcludedBotSet.has(qq)), `群 ${expected.groupId} 成员明细仍含机器人 QQ。`);
    originalCount += rawCount;
    eligibleCount += groupEligibleCount;
    excludedBotCount += groupExcludedBotCount;
    eligibleMembersAcrossGroups.push(...groupEligibleMembers);
    groupMemberCounts.push({
      groupId: expected.groupId,
      groupName,
      memberCount: rawCount,
      eligibleCount: groupEligibleCount,
      excludedBotCount: groupExcludedBotCount,
    });
  }

  const preview = previewQqWhitelistJson(text);
  reject(preview.totalCount !== preview.uniqueCount, "最终成员列表没有跨群去重。");
  reject(preview.duplicateCount !== 0, "成员列表包含重复 QQ。");
  reject(!Array.isArray(root.members), "成员列表缺失。");
  reject(root.members.some((qq) => typeof qq !== "string" || !/^[0-9]{5,12}$/.test(qq)), "成员列表包含无效 QQ。");
  reject(JSON.stringify(root.members) !== JSON.stringify([...root.members].sort()), "成员列表没有按 QQ 排序。");
  reject(root.members.some((qq) => configuredExcludedBotSet.has(qq)), "成员列表仍包含已配置的机器人 QQ。");
  const recomputedMembers = [...new Set(eligibleMembersAcrossGroups)].sort();
  reject(JSON.stringify(root.members) !== JSON.stringify(recomputedMembers), "最终成员列表不是两个来源群的严格去重并集。");

  reject(validation.original_count !== originalCount, "双群原始成员人数元数据不一致。");
  reject(validation.eligible_count !== eligibleCount, "双群排除机器人后人数元数据不一致。");
  reject(validation.unique_count !== preview.uniqueCount, "去重成员人数元数据不一致。");
  reject(validation.duplicate_count !== eligibleCount - preview.uniqueCount, "跨群重复成员人数元数据不一致。");
  reject(validation.excluded_bot_count !== excludedBotCount, "机器人排除人数元数据不一致。");
  reject(validation.invalid_count !== 0, "无效成员校验结果无效。");
  reject(validation.cross_group_count !== 0, "串群校验结果无效。");
  reject(JSON.stringify(validation.group_ids_seen) !== JSON.stringify(TARGET_GROUP_IDS), "成员所属群元数据无效。");
  const canonicalMembers = root.members.map((qq) => `${qq}\n`).join("");
  const snapshotSha256 = createHash("sha256").update(canonicalMembers, "utf8").digest("hex");
  reject(root.snapshot_sha256 !== snapshotSha256, "成员并集摘要不一致。");

  return {
    filePath: fullPath,
    memberCount: preview.uniqueCount,
    groupMemberCounts,
    crossGroupDuplicateCount: validation.duplicate_count,
    excludedBotCount,
    fetchedAt: source.fetched_at,
    snapshotSha256,
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
