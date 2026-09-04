#!/usr/bin/env sh
set -eu

bundle=${1:?缺少部署包路径}
deploy_dir=${2:?缺少部署目录}
enable_agent=${3:-false}

case "$bundle" in /tmp/grandumi-bug-bot-*.tar.gz) ;; *) exit 2 ;; esac
case "$deploy_dir" in /[A-Za-z0-9._/-]*) ;; *) exit 2 ;; esac
case "$enable_agent" in true|false) ;; *) exit 2 ;; esac

stamp=$(date +%Y%m%d%H%M%S)
stage="/tmp/grandumi-bug-bot-stage-$stamp"
backup="$deploy_dir/.deploy-backup-$stamp"
script_path="/tmp/grandumi-deploy-bug-bot-${bundle##*-}"
files=".dockerignore .env.example Dockerfile docker-compose.yml napcat-init.sh requirements.txt bot.py storage.py abuse_moderation.py qq_whitelist_sync.py github_issue.py agent_bridge.py media_pipeline.py export_by_date.py mark.py dedup.py config.server.example.json"

cleanup() {
  rm -rf "$stage"
  rm -f "$bundle"
}
trap cleanup EXIT INT TERM

mkdir -p "$stage" "$backup"
tar -xzf "$bundle" -C "$stage"
for name in $files; do
  test -f "$stage/$name"
  if test -f "$deploy_dir/$name"; then
    cp -p "$deploy_dir/$name" "$backup/$name"
  fi
  install -m 0644 "$stage/$name" "$deploy_dir/$name"
done

container_switch_started=false
rollback() {
  echo "机器人部署验证失败，开始回滚。" >&2
  for name in $files; do
    if test -f "$backup/$name"; then
      cp -p "$backup/$name" "$deploy_dir/$name"
    else
      rm -f "$deploy_dir/$name"
    fi
  done
  if test -f "$backup/config.server.json"; then
    rollback_config="$deploy_dir/.config.server.json.rollback-$stamp"
    cp -p "$backup/config.server.json" "$rollback_config"
    mv -f "$rollback_config" "$deploy_dir/config.server.json"
  fi
  if test "$container_switch_started" = "true"; then
    cd "$deploy_dir"
    docker compose build bug-bot >/dev/null 2>&1 || true
    docker compose up -d --no-deps --force-recreate bug-bot >/dev/null 2>&1 || true
  fi
  exit 1
}

if test -f "$deploy_dir/config.server.json"; then
  cp -p "$deploy_dir/config.server.json" "$backup/config.server.json"
python3 - "$deploy_dir/config.server.json" "$enable_agent" <<'PY' || rollback
import json
import os
import stat
import sys
import tempfile

path = sys.argv[1]
enable_agent = sys.argv[2].lower() == "true"
source_stat = os.stat(path)
with open(path, "r", encoding="utf-8") as file:
    data = json.load(file)

def migrate_config(data):
    """幂等补齐 2 群作用域，并把白名单权威源迁移为固定双群两小时间隔。"""
    import copy

    if not isinstance(data, dict):
        raise ValueError("config.server.json 顶层必须是对象")

    migrated = copy.deepcopy(data)
    source_group = "297542853"
    target_group = "524996856"

    def mirror_group_scope(container, key):
        if key not in container:
            return
        groups = container[key]
        if not isinstance(groups, list):
            raise ValueError(f"{key} 必须是群号数组")
        normalized = set()
        for value in groups:
            if isinstance(value, bool):
                raise ValueError(f"{key} 包含无效群号")
            text = str(value or "").strip()
            if not text.isdigit() or int(text) <= 0:
                raise ValueError(f"{key} 包含无效群号")
            normalized.add(str(int(text)))
        # 原群不在当前作用域时保持原样；尤其不能把空列表的禁用语义改掉。
        if source_group in normalized and target_group not in normalized:
            groups.append(int(target_group))

    for key in (
        "allowed_groups",
        "abuse_moderation_groups",
        "group_add_auto_approval_groups",
    ):
        mirror_group_scope(migrated, key)

    # 顶层字段供旧版单连接配置及未逐连接覆盖的 primary 继续使用。
    mirror_group_scope(migrated, "new_member_welcome_groups")
    connections = migrated.get("assistant_connections")
    if connections is not None and not isinstance(connections, list):
        raise ValueError("assistant_connections 必须是数组")
    if isinstance(connections, list):
        for connection in connections:
            if not isinstance(connection, dict):
                continue
            connection_id = str(connection.get("id") or "").strip().lower()
            if connection_id == "primary":
                mirror_group_scope(connection, "new_member_welcome_groups")

    legacy_sync_group = migrated.get("qq_whitelist_sync_group_id")
    if legacy_sync_group is not None:
        if isinstance(legacy_sync_group, bool):
            raise ValueError("qq_whitelist_sync_group_id 包含无效群号")
        normalized_legacy = str(legacy_sync_group or "").strip()
        if normalized_legacy != source_group:
            raise ValueError("旧版白名单同步数据源不是固定原群，拒绝自动迁移")
    configured_sync_groups = migrated.get("qq_whitelist_sync_group_ids")
    if configured_sync_groups is not None:
        if not isinstance(configured_sync_groups, list):
            raise ValueError("qq_whitelist_sync_group_ids 必须是群号数组")
        normalized_sync_groups = []
        for value in configured_sync_groups:
            if isinstance(value, bool):
                raise ValueError("qq_whitelist_sync_group_ids 包含无效群号")
            text = str(value or "").strip()
            if not text.isdigit() or int(text) <= 0:
                raise ValueError("qq_whitelist_sync_group_ids 包含无效群号")
            normalized_sync_groups.append(str(int(text)))
        if normalized_sync_groups != [source_group, target_group]:
            raise ValueError("白名单同步群集合与固定双群不一致，拒绝覆盖")
    migrated["qq_whitelist_sync_group_ids"] = [
        int(source_group), int(target_group)
    ]
    migrated["qq_whitelist_sync_interval_hours"] = 2
    return migrated

data = migrate_config(data)
data["agent_enabled"] = enable_agent
data["agent_owner_qq"] = 651846226
data["agent_notification_interval_seconds"] = 3
data["chat_agent_enabled"] = True
data["admin_agent_enabled"] = True
data["admin_agent_owner_qq"] = 651846226
data["admin_agent_max_content_length"] = 3000
data["chat_max_content_length"] = 500
data["chat_max_pending_per_user"] = 1
data["chat_cooldown_seconds"] = 15
directory = os.path.dirname(os.path.abspath(path))
fd, tmp = tempfile.mkstemp(
    prefix=".config.server.json.", suffix=".new", dir=directory
)
try:
    with os.fdopen(fd, "w", encoding="utf-8") as file:
        json.dump(data, file, ensure_ascii=False, indent=2)
        file.write("\n")
        file.flush()
        os.fchown(file.fileno(), source_stat.st_uid, source_stat.st_gid)
        os.fchmod(file.fileno(), stat.S_IMODE(source_stat.st_mode))
        os.fsync(file.fileno())
    os.replace(tmp, path)
    tmp = None
    directory_flags = os.O_RDONLY | getattr(os, "O_DIRECTORY", 0)
    directory_fd = os.open(directory, directory_flags)
    try:
        os.fsync(directory_fd)
    finally:
        os.close(directory_fd)
finally:
    if tmp is not None:
        try:
            os.unlink(tmp)
        except FileNotFoundError:
            pass
PY
fi

cd "$deploy_dir"
docker compose config -q || rollback
docker compose build bug-bot || rollback
container_switch_started=true
docker compose up -d --no-deps --force-recreate bug-bot || rollback
sleep 5
running=$(docker inspect -f '{{.State.Running}}' grandumi-qq-bug-bot 2>/dev/null || true)
test "$running" = "true" || rollback
docker compose exec -T bug-bot python agent_bridge.py status >/tmp/grandumi-agent-bridge-status.txt 2>&1 || rollback
grep -q '"ok": true' /tmp/grandumi-agent-bridge-status.txt || rollback
rm -f /tmp/grandumi-agent-bridge-status.txt
rm -rf "$backup"
echo "BUG_BOT_DEPLOY_OK"
