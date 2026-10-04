#!/usr/bin/env bash
set -Eeuo pipefail

source_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
repo="${1:-}"
target="${2:-}"
publish_dir="${3:-}"
include_deployed="${4:-}"
deployed_file="${5:-/var/lib/grandumi-production-deployed}"
persist_root="${6:-/data/grandumi/Persist}"

die() { echo "错误：$*" >&2; exit 1; }

[[ -d "$repo/.git" || -f "$repo/.git" ]] || die "正式服仓库不存在：$repo"
[[ "$target" =~ ^[0-9a-f]{40}$ ]] || die "必须提供 40 位目标提交号"
[[ -d "$publish_dir" ]] || die "后端发布目录不存在：$publish_dir"
[[ "$include_deployed" == 0 || "$include_deployed" == 1 ]] \
  || die "include_deployed 必须为 0 或 1"
[[ -d "$persist_root" ]] || die "对局日志目录不存在：$persist_root"

# shellcheck source=ops/server/grandumi-builtin-recovery-compat.sh
source "$source_root/ops/server/grandumi-builtin-recovery-compat.sh"

deployed=""
alias=""
commit=""
changed_path=""
ids_file="$publish_dir/.builtin-ruleset-recovery-aliases.ids"
changed_file="$publish_dir/.builtin-ruleset-recovery-aliases.changed"
manifest="$publish_dir/builtin-ruleset-recovery-aliases.json"
: > "$ids_file"

# 未声明已安全排空时，构建期间旧进程仍可能创建房间，因此必须纳入当前正式提交。
if [[ "$include_deployed" == 1 ]]; then
  deployed="$(tr -d '\r\n' < "$deployed_file" 2>/dev/null || true)"
  if [[ "$deployed" =~ ^[0-9a-f]{40}$ && "$deployed" != "$target" ]]; then
    printf 'builtin-%s\n' "$deployed" >> "$ids_file"
  fi
fi

# 日志首行在建房时一次性写入，读取它不会与后续动作追加竞争。即使排空门禁已通过，
# 这里仍扫描一次；只要存在旧日志，就必须按原规则执行兼容检查，绝不扩大白名单。
python3 - "$persist_root" >> "$ids_file" <<'PY'
import json
import pathlib
import re
import sys

root = pathlib.Path(sys.argv[1])
pattern = re.compile(r"builtin-[0-9a-f]{40}")
for path in sorted(root.glob("*.jsonl")):
    try:
        with path.open("r", encoding="utf-8") as handle:
            header = json.loads(handle.readline())
    except Exception as exc:
        raise SystemExit(f"无法读取房间规则版本 {path.name}：{exc}")
    if header.get("kind") != "create":
        raise SystemExit(f"房间日志首行不是建房记录：{path.name}")
    ruleset_id = header.get("rulesetId")
    if isinstance(ruleset_id, str) and pattern.fullmatch(ruleset_id):
        print(ruleset_id)
PY
sort -u -o "$ids_file" "$ids_file"

aliases=()
while IFS= read -r alias; do
  alias="${alias%$'\r'}"
  [[ -n "$alias" && "$alias" != "builtin-$target" ]] || continue
  [[ "$alias" =~ ^builtin-[0-9a-f]{40}$ ]] || die "内置规则恢复别名格式无效：$alias"
  commit="${alias#builtin-}"
  git -C "$repo" cat-file -e "$commit^{commit}" 2>/dev/null \
    || die "内置规则恢复别名提交不存在：$commit"
  git -C "$repo" merge-base --is-ancestor "$commit" "$target" \
    || die "内置规则恢复别名不是目标提交祖先：$commit -> $target"

  git -C "$repo" -c core.quotePath=false diff --name-only -z \
    "$commit" "$target" -- 服务端WebSocket 卡牌数据 > "$changed_file"
  while IFS= read -r -d '' changed_path; do
    grandumi_is_builtin_recovery_compatible_change \
      "$repo" "$commit" "$target" "$changed_path" \
      || die "旧内置规则 $alias 与目标版本存在未授权服务端/卡表差异：$changed_path"
  done < "$changed_file"
  aliases+=("$alias")
done < "$ids_file"

python3 - "$manifest" "builtin-$target" "${aliases[@]}" <<'PY'
import json
import os
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
target = sys.argv[2]
aliases = sys.argv[3:]
document = {
    "schema": "grandumi.builtin-ruleset-recovery-aliases.v1",
    "targetRulesetId": target,
    "aliases": aliases,
}
temporary = path.with_name(path.name + ".next")
temporary.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
os.replace(temporary, path)
PY
rm -f "$ids_file" "$changed_file"
[[ -s "$manifest" ]] || die "内置规则恢复别名清单生成失败"
echo "内置规则恢复别名已绑定目标提交：${aliases[*]:-无}"
