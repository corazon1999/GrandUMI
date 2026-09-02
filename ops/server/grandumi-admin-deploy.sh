#!/usr/bin/env bash
set -Eeuo pipefail

queue_dir=/var/lib/grandumi-admin-deploy/requests
status_dir=/var/lib/grandumi-admin-deploy/status
git_url=https://github.com/corazon1999/GrandUMI.git
lock_file=/run/lock/grandumi-admin-deploy.lock

write_status() {
  local environment="$1" state="$2" target="${3:-}" message="$4"
  local encoded temporary
  encoded="$(printf '%s' "$message" | base64 -w0)"
  temporary="$status_dir/.${environment}.status.$$"
  printf 'state=%s\ntarget=%s\nmessage=%s\nupdated=%s\n' \
    "$state" "$target" "$encoded" "$(date +%s)" > "$temporary"
  chmod 0644 "$temporary"
  mv -f "$temporary" "$status_dir/$environment.status"
}

production_snapshot() {
  local active port
  active="$(cat /var/lib/grandumi-ha/active-slot 2>/dev/null || echo a)"
  port=8080
  [[ "$active" == b ]] && port=8082
  curl -fsS "http://127.0.0.1:$port/ready" | python3 -c \
    'import json,sys; d=json.load(sys.stdin); print(str(bool(d.get("maintenance"))).lower(), int(d.get("rooms", -1)))'
}

deploy_test() {
  local repo=/opt/grandumi-test
  git -C "$repo" fetch --force --prune "$git_url" \
    refs/heads/main:refs/remotes/admin/main || return 1
  target="$(git -C "$repo" rev-parse refs/remotes/admin/main)" || return 1
  [[ "$target" =~ ^[0-9a-f]{40}$ ]] || { echo "远端 main 提交号无效" >&2; return 1; }
  git -C "$repo" show "$target:ops/server/deploy-test.sh" | bash -s -- "$target" all || return 1
}

deploy_production() {
  local repo=/opt/grandumi tested pending snapshot stage_script activate_script
  git -C "$repo" fetch --force --prune "$git_url" \
    refs/heads/main:refs/remotes/admin/main || return 1
  target="$(git -C "$repo" rev-parse refs/remotes/admin/main)" || return 1
  [[ "$target" =~ ^[0-9a-f]{40}$ ]] || { echo "远端 main 提交号无效" >&2; return 1; }

  tested="$(tr -d '\r\n' < /var/lib/grandumi-test-release/test-deployed 2>/dev/null || true)"
  [[ "$tested" == "$target" ]] || {
    echo "正式发布被拒绝：最新版本尚未部署到测试服（测试服 ${tested:-无记录}，目标 $target）" >&2
    return 1
  }
  pending="$(git -C "$repo" ls-tree -r --name-only "$target" -- changelog-cache/pending | grep -E '\.md$' || true)"
  [[ -z "$pending" ]] || {
    echo "正式发布被拒绝：changelog-cache/pending 仍有待发布记录，请先汇总并归档" >&2
    return 1
  }
  snapshot="$(production_snapshot)" || return 1
  [[ "$snapshot" == "true 0" ]] || {
    echo "正式发布被拒绝：必须处于维护模式且进行中房间为 0（当前 $snapshot）" >&2
    return 1
  }

  stage_script="$status_dir/stage-$target.sh"
  activate_script="$status_dir/activate-$target.sh"
  git -C "$repo" show "$target:ops/server/stage-grandumi-production.sh" > "$stage_script" || return 1
  git -C "$repo" show "$target:ops/server/activate-grandumi-production.sh" > "$activate_script" || {
    rm -f "$stage_script"
    return 1
  }
  chmod 0700 "$stage_script" "$activate_script" || {
    rm -f "$stage_script" "$activate_script"
    return 1
  }
  if ! bash "$stage_script" "$target"; then
    rm -f "$stage_script" "$activate_script"
    return 1
  fi
  snapshot="$(production_snapshot)" || {
    rm -f "$stage_script" "$activate_script"
    return 1
  }
  [[ "$snapshot" == "true 0" ]] || {
    rm -f "$stage_script" "$activate_script"
    echo "正式发布切槽前检查失败：维护状态或进行中房间发生变化" >&2
    return 1
  }
  if ! bash "$activate_script" "$target"; then
    rm -f "$stage_script" "$activate_script"
    return 1
  fi
  rm -f "$stage_script" "$activate_script"
}

deploy_hex_catalog() {
  local request="$1" environment="$2" nonce="$3"
  target="$(python3 - "$request" "$environment" "$nonce" <<'PY'
import hashlib
import json
import os
import grp
import sys
import time

request_path, expected_environment, expected_nonce = sys.argv[1:]
active_path = {
    "test": "/data/grandumi-test/hex-catalog/active.json",
    "production": "/data/grandumi/hex-catalog/active.json",
}[expected_environment]
allowed_tiers = {"Silver", "Gold", "Rainbow"}
known_ids = set(range(1, 57))
alternative_ids = {30, 48}
retired_ids = {27}
required_regular_counts = {"Silver": 18, "Gold": 18, "Rainbow": 17}
built_in_digest = "sha256:b466b6465456221da8edbb2eaee26df5771b5ed07b2002d77c5892a145b8b430"

def fail(message):
    raise SystemExit(message)

def canonical_digest(tiers):
    canonical = "".join(f"{item['id']}:{item['tier']}\n" for item in sorted(tiers, key=lambda value: value["id"]))
    return "sha256:" + hashlib.sha256(canonical.encode("utf-8")).hexdigest()

def validate_tiers(tiers, label, require_current_balance=True):
    if not isinstance(tiers, list) or len(tiers) != len(known_ids):
        fail(f"{label}必须包含完整目录")
    seen = set()
    for item in tiers:
        if not isinstance(item, dict) or set(item) != {"id", "tier"}:
            fail(f"{label}包含无效品质项")
        hex_id, tier = item.get("id"), item.get("tier")
        if type(hex_id) is not int or hex_id not in known_ids or hex_id in seen or tier not in allowed_tiers:
            fail(f"{label}包含未知、重复编号或无效品质")
        seen.add(hex_id)
    if require_current_balance:
        for tier, required in required_regular_counts.items():
            count = sum(1 for item in tiers
                        if item["id"] not in alternative_ids
                        and item["id"] not in retired_ids
                        and item["tier"] == tier)
            if count != required:
                fail(f"{label}的 {tier} 常规海克斯必须恰好为 {required} 个，当前为 {count} 个，拒绝激活")

with open(request_path, "r", encoding="utf-8") as source:
    request = json.load(source)
if request.get("schema") != "grandumi.admin.hex-catalog-request.v1" or request.get("kind") != "hex-catalog":
    fail("海克斯配置请求 schema 无效")
if request.get("environment") != expected_environment or request.get("nonce") != expected_nonce:
    fail("海克斯配置请求环境或 nonce 校验失败")
actor = request.get("actor")
request_id = request.get("requestId")
if not isinstance(actor, str) or not actor or len(actor) > 64 or any(ord(ch) < 32 for ch in actor):
    fail("海克斯配置请求操作者无效")
if not isinstance(request_id, str) or not request_id or len(request_id) > 128 or any(ord(ch) < 32 for ch in request_id):
    fail("海克斯配置请求编号无效")
draft_revision = request.get("draftRevision")
expected_revision = request.get("expectedActiveRevision")
expected_digest = request.get("expectedActiveDigest")
tiers = request.get("tiers")
if type(draft_revision) is not int or draft_revision < 1 or type(expected_revision) is not int or expected_revision < 0:
    fail("海克斯配置请求版本无效")
if not isinstance(expected_digest, str) or not expected_digest.startswith("sha256:") or len(expected_digest) != 71:
    fail("海克斯配置请求基线摘要无效")
validate_tiers(tiers, "海克斯配置请求")
digest = canonical_digest(tiers)
if request.get("digest") != digest:
    fail("海克斯配置请求内容摘要不一致")

current_revision = 0
current_digest = built_in_digest
if os.path.exists(active_path):
    with open(active_path, "r", encoding="utf-8") as source:
        current = json.load(source)
    if current.get("schema") != "grandumi.hex-catalog.v1":
        fail("目标环境 active 海克斯配置 schema 无效")
    current_revision = current.get("revision")
    current_digest = current.get("digest")
    current_tiers = current.get("tiers")
    current_source_draft_revision = current.get("sourceDraftRevision")
    if type(current_revision) is not int or current_revision < 1:
        fail("目标环境 active 海克斯配置版本无效")
    if type(current_source_draft_revision) is not int or current_source_draft_revision < 1:
        fail("目标环境 active 海克斯配置草稿版本无效")
    # 旧 active 创建时仍把编号 27 计入 18/18/18；代码升级不能因此拒绝启动或阻断下一次调配。
    validate_tiers(current_tiers, "目标环境 active 海克斯配置", require_current_balance=False)
    if not isinstance(current_digest, str) or current_digest != canonical_digest(current_tiers):
        fail("目标环境 active 海克斯配置摘要无效")
    if current.get("sourceRequestId") == request_id:
        if (current_digest != digest
                or current.get("sourceDraftRevision") != draft_revision
                or current.get("publishedBy") != actor):
            fail("重复请求编号对应了不同内容")
        print(digest)
        raise SystemExit(0)
if current_revision != expected_revision or current_digest != expected_digest:
    fail(f"目标环境海克斯配置版本冲突：当前 v{current_revision}，请求基于 v{expected_revision}")

published = {
    "schema": "grandumi.hex-catalog.v1",
    "revision": current_revision + 1,
    "digest": digest,
    "sourceDraftRevision": draft_revision,
    "sourceRequestId": request_id,
    "publishedAt": int(time.time() * 1000),
    "publishedBy": actor,
    "tiers": sorted(tiers, key=lambda value: value["id"]),
}
directory = os.path.dirname(active_path)
os.makedirs(directory, mode=0o750, exist_ok=True)
gid = grp.getgrnam("grandumi").gr_gid
os.chown(directory, 0, gid)
os.chmod(directory, 0o750)
temporary = os.path.join(directory, f".active.{expected_nonce}.tmp")
data = (json.dumps(published, ensure_ascii=False, indent=2, separators=(",", ": ")) + "\n").encode("utf-8")
fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o640)
try:
    with os.fdopen(fd, "wb", closefd=True) as target_file:
        target_file.write(data)
        target_file.flush()
        os.fsync(target_file.fileno())
    os.chown(temporary, 0, gid)
    os.replace(temporary, active_path)
    directory_fd = os.open(directory, os.O_RDONLY | getattr(os, "O_DIRECTORY", 0))
    try:
        os.fsync(directory_fd)
    finally:
        os.close(directory_fd)
finally:
    try:
        os.unlink(temporary)
    except FileNotFoundError:
        pass
print(digest)
PY
)" || return 1
  [[ "$target" =~ ^sha256:[0-9a-f]{64}$ ]] || {
    echo "海克斯配置执行器没有返回有效摘要" >&2
    return 1
  }
}

process_request() {
  local request="$1" filename environment nonce request_environment request_nonce log_file message status_key request_kind result target
  filename="$(basename "$request")"
  if [[ ! -f "$request" || -L "$request" ]]; then
    rm -f -- "$request"
    return 0
  fi

  if [[ "$filename" =~ ^(test|production)-([0-9a-f]{32})\.request$ ]]; then
    request_kind=site
    environment="${BASH_REMATCH[1]}"
    nonce="${BASH_REMATCH[2]}"
    status_key="$environment"
    request_environment="$(sed -n 's/^environment=//p' "$request")"
    request_nonce="$(sed -n 's/^nonce=//p' "$request")"
  elif [[ "$filename" =~ ^hex-(test|production)-([0-9a-f]{32})\.request$ ]]; then
    request_kind=hex
    environment="${BASH_REMATCH[1]}"
    nonce="${BASH_REMATCH[2]}"
    status_key="hex-$environment"
    request_environment="$environment"
    request_nonce="$nonce"
  else
    rm -f -- "$request"
    return 0
  fi

  if [[ "$request_environment" != "$environment" || "$request_nonce" != "$nonce" ]]; then
    write_status "$status_key" failed "" "发布请求校验失败，未执行任何操作。"
    rm -f -- "$request"
    return 0
  fi

  target=""
  if [[ "$request_kind" == hex ]]; then
    write_status "$status_key" running "" "正在校验草稿基线并原子激活海克斯配置。"
  else
    write_status "$status_key" running "" "正在获取远端 main 最新版本并执行安全检查。"
  fi
  log_file="$(mktemp "/run/grandumi-admin-${status_key}.XXXXXX.log")"
  if [[ "$request_kind" == hex ]]; then
    if deploy_hex_catalog "$request" "$environment" "$nonce" >"$log_file" 2>&1; then
      result=0
    else
      result=$?
    fi
  else
    if "deploy_$environment" >"$log_file" 2>&1; then
      result=0
    else
      result=$?
    fi
  fi
  if [[ "$result" == 0 ]]; then
    message="$(tail -n 1 "$log_file")"
    [[ "$request_kind" == hex ]] && message="海克斯配置已原子激活；仅新建房间使用新版本。"
    write_status "$status_key" success "$target" "${message:-发布成功。}"
  else
    message="$(tail -n 3 "$log_file" | tr '\n' ' ' | sed 's/[[:space:]]\+/ /g')"
    write_status "$status_key" failed "$target" "${message:-发布失败，请查看服务器日志。}"
  fi
  rm -f "$log_file" "$request"
}

install -d -m 0755 "$status_dir"
install -d -o grandumi -g grandumi -m 0750 "$queue_dir"
exec 9>"$lock_file"
flock -n 9 || exit 0
while IFS= read -r -d '' request; do
  process_request "$request"
done < <(find "$queue_dir" -maxdepth 1 -type f -name '*.request' -print0 | sort -z)
