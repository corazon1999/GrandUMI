#!/usr/bin/env bash
set -Eeuo pipefail

state_dir=/var/lib/grandumi-ha
release_root=/opt/grandumi/releases
slot_root=/opt/grandumi/slots
active_file="$state_dir/active-slot"
standby_file="$state_dir/standby-slot"
lock_file="$state_dir/switch.lock"
account_cutover_lock=/run/lock/grandumi-account-authority-cutover.lock
mode="${1:-}"
release="${2:-}"
previous_target_backend=""
previous_target_frontend=""
test_backend_was_active=0
shared_authority_committed=0
old_backend_stopped=0
drained_release=0
proxy_switch_started=0
shared_active_marker=/data/grandumi-shared/active
shared_migration=/usr/local/sbin/grandumi-shared-account-migration
direct_release="${GRANDUMI_PRODUCTION_DIRECT:-0}"
backend_ready_timeout_seconds="${GRANDUMI_PRODUCTION_BACKEND_READY_TIMEOUT_SECONDS:-600}"
backend_progress_interval_seconds=10

die() { echo "错误：$*" >&2; exit 1; }
verify_direct_account_authority_after_stop() {
  local target_backend="$1"
  [[ -f "$shared_active_marker" ]] \
    || die "Direct 正式发布在旧后端停写后发现共享账号 active 标记缺失"
  "$shared_migration" verify-target "$target_backend"
}
[[ "$direct_release" == 0 || "$direct_release" == 1 ]] \
  || die "Direct 正式发布进程标记无效"
if [[ "$direct_release" == 1 ]]; then
  [[ "$mode" == --release ]] || die "Direct 正式发布不允许用于自动故障转移"
  [[ -f "$shared_active_marker" ]] \
    || die "Direct 正式发布只允许在共享账号权威已经激活后执行"
fi
[[ "$backend_ready_timeout_seconds" =~ ^[0-9]+$ ]] \
  || die "新槽后端就绪超时必须是整数秒"
(( backend_ready_timeout_seconds >= 60 && backend_ready_timeout_seconds <= 900 )) \
  || die "新槽后端就绪超时必须在 60 到 900 秒之间"
verify_qq_access_rollback_compatibility() {
  local target_backend="$1"
  local players_db=/data/grandumi/players.db
  local table_exists initialized marker
  if [[ -f "$shared_active_marker" ]]; then
    [[ -f "$target_backend/.grandumi-shared-account-v1" ]] || die \
      "共享账号库已激活，目标槽位不兼容，拒绝切换或回滚"
  fi
  [[ -s "$players_db" ]] || return 0
  command -v sqlite3 >/dev/null || die "缺少 sqlite3，无法验证 QQ 准入回滚兼容性"

  table_exists="$(sqlite3 -readonly "$players_db" \
    "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='qq_whitelist_state';")"
  [[ "$table_exists" == 0 || "$table_exists" == 1 ]] \
    || die "无法判定 players.db 的 QQ 白名单结构"
  [[ "$table_exists" == 1 ]] || return 0

  initialized="$(sqlite3 -readonly "$players_db" \
    'SELECT count(*) FROM qq_whitelist_state WHERE singleton_id=1;')"
  [[ "$initialized" == 0 || "$initialized" == 1 ]] \
    || die "players.db 的 QQ 白名单状态异常"
  [[ "$initialized" == 1 ]] || return 0

  marker="$target_backend/.grandumi-qq-access-enforcement-v1"
  [[ -f "$marker" ]] || die \
    "QQ 白名单已生效，目标槽位不具备准入校验能力，拒绝回退到旧版本"
}
verify_ruleset_recovery_alias_manifest() {
  local target_backend="$1" release="$2"
  local manifest="$target_backend/builtin-ruleset-recovery-aliases.json"
  [[ -s "$manifest" ]] || die "目标后端缺少内置规则恢复别名清单"
  python3 - "$manifest" "builtin-$release" <<'PY'
import json
import re
import sys

with open(sys.argv[1], "r", encoding="utf-8") as handle:
    document = json.load(handle)
if document.get("schema") != "grandumi.builtin-ruleset-recovery-aliases.v1":
    raise SystemExit("内置规则恢复别名清单协议无效")
if document.get("targetRulesetId") != sys.argv[2]:
    raise SystemExit("内置规则恢复别名清单未绑定目标提交")
aliases = document.get("aliases")
if not isinstance(aliases, list) or len(aliases) > 32 or len(set(aliases)) != len(aliases):
    raise SystemExit("内置规则恢复别名清单数量或唯一性无效")
if any(not isinstance(item, str) or re.fullmatch(r"builtin-[0-9a-f]{40}", item) is None
       for item in aliases):
    raise SystemExit("内置规则恢复别名格式无效")
PY
}
verify_drained_release_contract() {
  local target_backend="$1" release="$2"
  local marker="$target_backend/.grandumi-production-drained-release-v1"
  local manifest="$target_backend/builtin-ruleset-recovery-aliases.json"
  local source_commit active_backend active_commit
  [[ -e "$marker" ]] || return 0
  [[ -s "$marker" && -s "$manifest" ]] || die "排空发布契约或恢复别名清单缺失"
  source_commit="$(python3 - "$marker" "$manifest" "$release" <<'PY'
import json
import re
import sys

marker_path, manifest_path, target = sys.argv[1:]
with open(marker_path, "r", encoding="utf-8") as handle:
    marker = json.load(handle)
if marker.get("schema") != "grandumi.production-drained-release.v1":
    raise SystemExit("排空发布契约协议无效")
source = marker.get("sourceCommit")
if re.fullmatch(r"[0-9a-f]{40}", source or "") is None:
    raise SystemExit("排空发布契约的源版本无效")
if marker.get("targetCommit") != target or marker.get("requiresEmptyPersist") is not True:
    raise SystemExit("排空发布契约未绑定目标提交或空日志要求")
with open(manifest_path, "r", encoding="utf-8") as handle:
    manifest = json.load(handle)
if manifest.get("targetRulesetId") != f"builtin-{target}" or manifest.get("aliases") != []:
    raise SystemExit("排空发布只能绑定空恢复别名清单")
print(source)
PY
)" || die "排空发布契约校验失败"
  active_backend="$(readlink -f "$slot_root/$active/backend" 2>/dev/null || true)"
  [[ "$active_backend" =~ ^${release_root}/([0-9a-f]{40})/backend$ ]] \
    || die "无法判定排空发布的当前活动版本：$active_backend"
  active_commit="${BASH_REMATCH[1]}"
  [[ "$active_commit" == "$source_commit" ]] \
    || die "排空发布源版本已变化：契约 $source_commit，活动槽 $active_commit"
  [[ -x /usr/local/sbin/grandumi-production-drained-state ]] \
    || die "缺少正式服排空状态校验工具"
  drained_release=1
}
verify_empty_persist_after_old_backend_stop() {
  local active_journal
  systemctl is-active --quiet "grandumi-production-backend@$active.service" \
    && { echo "旧后端仍在运行，无法关闭排空发布竞争窗口" >&2; return 1; }
  active_journal="$(find /data/grandumi/Persist -maxdepth 1 -type f -name '*.jsonl' -print -quit)"
  [[ -z "$active_journal" ]] || {
    echo "旧后端停写后发现活动对局日志，拒绝启动新规则版本：$active_journal" >&2
    return 1
  }
  echo "旧后端已停写且活动对局日志仍为 0，排空发布竞争窗口已关闭。"
}
protect_rollback_incompatible_recovery() {
  local active_backend supported_ruleset hold_root
  active_backend="$(readlink -f "$slot_root/$active/backend" 2>/dev/null || true)"
  [[ "$active_backend" =~ ^${release_root}/([0-9a-f]{40})/backend$ ]] \
    || die "无法判定回退槽位的内置规则版本：$active_backend"
  supported_ruleset="builtin-${BASH_REMATCH[1]}"
  hold_root="/data/grandumi/Persist/rollback-hold/$(date -u +%Y%m%dT%H%M%SZ)-${active}-to-${target}-$$"

  # 新槽在监听前只会恢复已有日志，不会接收公网建房；若启动失败，把旧槽无法识别的
  # builtin 日志与快照成对移入保全目录，再启动旧槽。这样既不改写原规则 ID，也不会
  # 让旧二进制把尚未处理完的恢复队列误判为损坏并再次隔离。
  python3 - /data/grandumi/Persist "$hold_root" "$supported_ruleset" <<'PY'
import datetime
import hashlib
import json
import os
import pathlib
import re
import stat
import sys

persist = pathlib.Path(sys.argv[1]).resolve()
hold = pathlib.Path(sys.argv[2])
supported = sys.argv[3]
expected_hold_parent = (persist / "rollback-hold").resolve()
if persist != pathlib.Path("/data/grandumi/Persist"):
    raise SystemExit(f"持久化目录安全检查失败：{persist}")
if hold.parent.resolve() != expected_hold_parent:
    raise SystemExit(f"回退保全目录安全检查失败：{hold}")
if re.fullmatch(r"builtin-[0-9a-f]{40}", supported) is None:
    raise SystemExit(f"回退槽位规则版本无效：{supported}")

def digest(path: pathlib.Path) -> str:
    hasher = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            hasher.update(block)
    return hasher.hexdigest()

candidates = []
for journal in sorted(persist.glob("*.jsonl")):
    info = journal.lstat()
    if not stat.S_ISREG(info.st_mode) or journal.is_symlink():
        raise SystemExit(f"恢复日志不是普通文件：{journal}")
    if re.fullmatch(r"[0-9a-f]{12}\.jsonl", journal.name) is None:
        raise SystemExit(f"恢复日志文件名无效：{journal.name}")
    try:
        with journal.open("r", encoding="utf-8") as handle:
            header = json.loads(handle.readline())
    except Exception as exc:
        raise SystemExit(f"无法读取恢复日志规则版本 {journal.name}：{exc}") from exc
    if header.get("kind") != "create":
        raise SystemExit(f"恢复日志首行不是建房记录：{journal.name}")
    ruleset = header.get("rulesetId")
    if (isinstance(ruleset, str)
            and re.fullmatch(r"builtin-[0-9a-f]{40}", ruleset)
            and ruleset != supported):
        candidates.append((journal, ruleset))

if not candidates:
    print(f"回退保全检查通过：没有 {supported} 无法识别的 builtin 恢复日志")
    raise SystemExit(0)

hold.mkdir(mode=0o700, parents=True, exist_ok=False)
records = []
for journal, ruleset in candidates:
    room_id = journal.stem
    snapshot = persist / f"{room_id}.snapshot.json"
    pair = []
    for kind, source in (("journal", journal), ("snapshot", snapshot)):
        if not source.exists():
            if kind == "snapshot":
                continue
            raise SystemExit(f"回退保全源文件缺失：{source}")
        info = source.lstat()
        if not stat.S_ISREG(info.st_mode) or source.is_symlink():
            raise SystemExit(f"回退保全源文件不是普通文件：{source}")
        pair.append({
            "kind": kind,
            "name": source.name,
            "size": info.st_size,
            "sha256": digest(source),
        })

    records.append({"roomId": room_id, "rulesetId": ruleset, "files": pair})

# 全部源文件通过预检后才开始移动；先移快照、最后移日志。中途失败时回退函数
# 会拒绝启动旧槽，避免旧二进制扫描到未受保护的日志。
for record in records:
    for item in sorted(record["files"], key=lambda value: value["kind"] == "journal"):
        source = persist / item["name"]
        destination = hold / item["name"]
        os.replace(source, destination)
        if digest(destination) != item["sha256"]:
            raise SystemExit(f"回退保全文件移动后哈希不一致：{destination}")

manifest = {
    "schema": "grandumi.rollback-recovery-hold.v1",
    "createdAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat().replace("+00:00", "Z"),
    "supportedRulesetId": supported,
    "roomCount": len(records),
    "fileCount": sum(len(record["files"]) for record in records),
    "records": records,
}
temporary = hold / "manifest.json.next"
with temporary.open("w", encoding="utf-8") as handle:
    json.dump(manifest, handle, ensure_ascii=False, indent=2)
    handle.write("\n")
    handle.flush()
    os.fsync(handle.fileno())
os.replace(temporary, hold / "manifest.json")
complete = hold / "HOLD_COMPLETE.next"
with complete.open("w", encoding="utf-8") as handle:
    handle.write("complete\n")
    handle.flush()
    os.fsync(handle.fileno())
os.replace(complete, hold / "HOLD_COMPLETE")
print(
    f"回退前已保全旧槽不兼容恢复日志：{len(records)} 个房间，"
    f"{manifest['fileCount']} 个文件，目录 {hold}"
)
PY
}
mkdir -p "$state_dir" "$slot_root/a" "$slot_root/b"
exec 9>"$lock_file"
flock -n 9 || die "另一个切换任务正在执行"
exec 8>"$account_cutover_lock"
flock -n 8 || die "测试后端部署或另一账号权威切换正在进行"

active="$(cat "$active_file" 2>/dev/null || true)"
if [[ "$direct_release" == 1 ]]; then
  direct_standby="$(cat "$standby_file" 2>/dev/null || true)"
  [[ "$active" =~ ^[ab]$ && "$direct_standby" =~ ^[ab]$ && "$active" != "$direct_standby" ]] \
    || die "Direct 正式发布要求有效且互异的 A/B 活动槽与备用槽"
  systemctl is-active --quiet "grandumi-production-backend@$active.service" \
    || die "Direct 正式发布要求当前正式后端槽健康运行"
  systemctl is-active --quiet "grandumi-production-frontend@$active.service" \
    || die "Direct 正式发布要求当前正式前端槽健康运行"
else
  [[ "$active" == a || "$active" == b ]] || active=a
fi
other=b; [[ "$active" == b ]] && other=a
# 首次权威提交后若进程在更新 active-slot 前退出，状态文件仍指向旧版槽位。
# 重跑时必须识别该恢复态并继续禁止回滚，而不能把旧槽误当作安全恢复目标。
if [[ -f "$shared_active_marker" \
    && ! -f "$slot_root/$active/backend/.grandumi-shared-account-v1" ]]; then
  shared_authority_committed=1
fi

case "$mode" in
  --release)
    [[ "$release" =~ ^[0-9a-f]{40}$ ]] || die "发布切换必须提供 40 位提交号"
    [[ -d "$release_root/$release/backend" && -d "$release_root/$release/frontend" ]] \
      || die "发布包不存在：$release"
    verify_qq_access_rollback_compatibility "$release_root/$release/backend"
    verify_ruleset_recovery_alias_manifest "$release_root/$release/backend" "$release"
    verify_drained_release_contract "$release_root/$release/backend" "$release"
    target="$other"
    previous_target_backend="$(readlink "$slot_root/$target/backend" 2>/dev/null || true)"
    previous_target_frontend="$(readlink "$slot_root/$target/frontend" 2>/dev/null || true)"
    ln -sfn "$release_root/$release/backend" "$slot_root/$target/backend"
    ln -sfn "$release_root/$release/frontend" "$slot_root/$target/frontend"
    ;;
  --failover)
    target="$(cat "$standby_file" 2>/dev/null || true)"
    [[ "$target" == a || "$target" == b ]] || die "尚无已知可用备用槽位"
    [[ "$target" != "$active" ]] || die "备用槽位不能与活动槽位相同"
    [[ -e "$slot_root/$target/backend/GrandUMIServer.dll" ]] || die "备用后端发布包不存在"
    verify_qq_access_rollback_compatibility "$slot_root/$target/backend"
    ;;
  *) die "用法：grandumi-production-switch --release <commit> | --failover" ;;
esac

[[ -x "$shared_migration" ]] || die "共享账号迁移工具未安装"
if [[ "$mode" == --release ]]; then
  [[ -f "$slot_root/$target/backend/.grandumi-shared-account-v1" ]] \
    || die "目标发布不兼容共享账号库"
fi
"$shared_migration" verify-target "$slot_root/$target/backend"
if [[ "$direct_release" == 0 \
    && ( "$mode" == --release || -f "$shared_active_marker" ) ]]; then
  "$shared_migration" verify-test
fi

backend_port=8080; frontend_port=3000
[[ "$target" == b ]] && backend_port=8082 && frontend_port=3002
old_backend_port=8080; old_frontend_port=3000
[[ "$active" == b ]] && old_backend_port=8082 && old_frontend_port=3002

write_proxy() {
  local backend="$1" frontend="$2" slot="$3"
  printf 'proxy_pass http://127.0.0.1:%s;\n' "$backend" \
    > /etc/nginx/snippets/grandumi-active-backend.conf.next
  printf 'proxy_pass http://127.0.0.1:%s;\n' "$frontend" \
    > /etc/nginx/snippets/grandumi-active-frontend.conf.next
  printf 'root /opt/grandumi/slots/%s/frontend/public;\n' "$slot" \
    > /etc/nginx/snippets/grandumi-active-assets.conf.next
  printf 'root /opt/grandumi/slots/%s/frontend;\n' "$slot" \
    > /etc/nginx/snippets/grandumi-active-frontend-files.conf.next
  mv /etc/nginx/snippets/grandumi-active-backend.conf.next \
    /etc/nginx/snippets/grandumi-active-backend.conf
  mv /etc/nginx/snippets/grandumi-active-frontend.conf.next \
    /etc/nginx/snippets/grandumi-active-frontend.conf
  mv /etc/nginx/snippets/grandumi-active-assets.conf.next \
    /etc/nginx/snippets/grandumi-active-assets.conf
  mv /etc/nginx/snippets/grandumi-active-frontend-files.conf.next \
    /etc/nginx/snippets/grandumi-active-frontend-files.conf
  nginx -t
  systemctl reload nginx
}

wait_for_backend_ready() {
  local slot="$1" port="$2" expected_release="$3"
  local unit="grandumi-production-backend@$slot.service"
  local target_backend
  local started_epoch expected_pid current_pid elapsed next_progress
  local ready version progress_lines progress_count latest
  if [[ ! "$expected_release" =~ ^[0-9a-f]{40}$ ]]; then
    target_backend="$(readlink -f "$slot_root/$slot/backend" 2>/dev/null || true)"
    [[ "$target_backend" =~ ^${release_root}/([0-9a-f]{40})/backend$ ]] \
      || { echo "无法判定新槽后端版本：$target_backend" >&2; return 1; }
    expected_release="${BASH_REMATCH[1]}"
  fi
  started_epoch="$(date +%s)"
  expected_pid="$(systemctl show "$unit" --property MainPID --value)"
  [[ "$expected_pid" =~ ^[1-9][0-9]*$ ]] \
    || { echo "新槽后端没有有效主进程：$unit" >&2; return 1; }
  elapsed=0
  next_progress=0

  while (( elapsed < backend_ready_timeout_seconds )); do
    systemctl is-active --quiet "$unit" \
      || { echo "新槽后端在就绪前退出：$unit" >&2; return 1; }
    current_pid="$(systemctl show "$unit" --property MainPID --value)"
    [[ "$current_pid" == "$expected_pid" ]] \
      || { echo "新槽后端在就绪前发生进程重启：$expected_pid -> $current_pid" >&2; return 1; }

    if ready="$(curl -fsS --max-time 1 "http://127.0.0.1:$port/ready" 2>/dev/null)"; then
      version="$(curl -fsS --max-time 1 "http://127.0.0.1:$port/version" 2>/dev/null)" \
        || { echo "新槽后端已监听但版本接口不可用" >&2; return 1; }
      python3 - "$expected_release" "$ready" "$version" <<'PY'
import json
import sys

expected, ready_text, version_text = sys.argv[1:]
ready = json.loads(ready_text)
version = json.loads(version_text)
recovery = ready.get("recovery", {})
if (ready.get("status") != "ready"
        or ready.get("storage", {}).get("healthy") is not True
        or recovery.get("pausedRooms", 0) != 0
        or recovery.get("journalQueueDepth", 0) != 0
        or recovery.get("snapshotQueueDepth", 0) != 0):
    raise SystemExit("新槽后端监听后仍未达到可切流状态")
if version.get("commit") != expected:
    raise SystemExit(
        f"新槽后端版本与目标提交不一致：{version.get('commit')} != {expected}"
    )
PY
      echo "新槽后端恢复完成并已监听：$unit，耗时 ${elapsed}s，PID $expected_pid"
      return 0
    fi

    if (( elapsed >= next_progress )); then
      progress_lines="$(journalctl -u "$unit" --since "@$started_epoch" --no-pager -o cat \
        | grep -E '\[Restore\] (已恢复对局|已隔离损坏日志|恢复完成)' || true)"
      progress_count="$(grep -c . <<<"$progress_lines" || true)"
      latest="$(journalctl -u "$unit" --since "@$started_epoch" --no-pager -o cat \
        | grep -E '\[Restore\]|\[网络\]' | tail -n 1 || true)"
      echo "等待新槽后端恢复/监听：${elapsed}/${backend_ready_timeout_seconds}s，PID $expected_pid，恢复进展 $progress_count，最近状态 ${latest:-尚无恢复日志}"
      next_progress=$((elapsed + backend_progress_interval_seconds))
    fi
    sleep 1
    elapsed=$(( $(date +%s) - started_epoch ))
  done

  echo "新槽后端在有界窗口 ${backend_ready_timeout_seconds}s 内未完成恢复并监听：$unit" >&2
  return 1
}

rollback() {
  local status=$?
  if [[ "$shared_authority_committed" == 1 ]]; then
    trap - ERR
    echo "切换失败，但共享账号权威已经不可逆提交；保持目标新版，禁止回滚旧本地账号库，请修复后重跑同一发布。" >&2
    exit "$status"
  fi
  systemctl stop "grandumi-production-backend@$target.service" \
    "grandumi-production-frontend@$target.service" || true
  if [[ "$old_backend_stopped" == 1 ]]; then
    protect_rollback_incompatible_recovery
  fi
  systemctl start "grandumi-production-backend@$active.service" \
    "grandumi-production-frontend@$active.service" || true
  if [[ "$proxy_switch_started" == 1 ]]; then
    write_proxy "$old_backend_port" "$old_frontend_port" "$active" || true
  fi
  if [[ "$mode" == --release ]]; then
    if [[ -n "$previous_target_backend" ]]; then
      ln -sfn "$previous_target_backend" "$slot_root/$target/backend"
    else
      rm -f "$slot_root/$target/backend"
    fi
    if [[ -n "$previous_target_frontend" ]]; then
      ln -sfn "$previous_target_frontend" "$slot_root/$target/frontend"
    else
      rm -f "$slot_root/$target/frontend"
    fi
  fi
  if [[ "$direct_release" == 0 \
      && "$test_backend_was_active" == 1 && ! -f "$shared_active_marker" ]]; then
    systemctl start grandumi-test-backend.service || true
  fi
  echo "切换失败，已尝试恢复槽位 $active" >&2
}

trap rollback ERR

# 前端可并行预热；后端受数据目录单写租约保护，必须先停旧后启新。
systemctl start "grandumi-production-frontend@$target.service"
curl -fsS --retry 15 --retry-delay 1 --retry-connrefused \
  "http://127.0.0.1:$frontend_port/" >/dev/null
if [[ "$direct_release" == 0 \
    && "$mode" == --release && ! -f "$shared_active_marker" ]] \
    && systemctl is-active --quiet grandumi-test-backend.service; then
  test_backend_was_active=1
  systemctl stop grandumi-test-backend.service
fi
if [[ "$drained_release" == 1 ]]; then
  /usr/local/sbin/grandumi-production-drained-state --live "正式切槽前排空复核"
fi
systemctl stop "grandumi-production-backend@$active.service"
old_backend_stopped=1
if [[ "$drained_release" == 1 ]]; then
  verify_empty_persist_after_old_backend_stop
fi
if [[ "$mode" == --release ]]; then
  if [[ "$direct_release" == 1 ]]; then
    verify_direct_account_authority_after_stop "$slot_root/$target/backend"
  else
    "$shared_migration" prepare "$slot_root/$target/backend"
    if [[ ! -f "$shared_active_marker" ]]; then
      "$shared_migration" commit-authority "$slot_root/$target/backend"
      shared_authority_committed=1
    fi
  fi
fi
systemctl start "grandumi-production-backend@$target.service"
wait_for_backend_ready "$target" "$backend_port" "$release"
proxy_switch_started=1
write_proxy "$backend_port" "$frontend_port" "$target"

systemctl stop "grandumi-production-frontend@$active.service" || true
systemctl enable "grandumi-production-backend@$target.service" \
  "grandumi-production-frontend@$target.service" >/dev/null
systemctl disable "grandumi-production-backend@$active.service" \
  "grandumi-production-frontend@$active.service" >/dev/null || true
printf '%s\n' "$active" > "$standby_file.next"
mv "$standby_file.next" "$standby_file"
printf '%s\n' "$target" > "$active_file.next"
mv "$active_file.next" "$active_file"
printf '0\n' > "$state_dir/health-failures"
trap - ERR
if [[ "$direct_release" == 0 \
    && ( "$mode" == --release || -f "$shared_active_marker" ) ]]; then
  "$shared_migration" activate-test
fi
echo "正式服已切换：$active -> $target（后端 $backend_port，前端 $frontend_port）"
