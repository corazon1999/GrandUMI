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
files=".dockerignore Dockerfile docker-compose.yml napcat-init.sh requirements.txt bot.py storage.py qq_whitelist_sync.py github_issue.py agent_bridge.py media_pipeline.py export_by_date.py mark.py dedup.py config.server.example.json"

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

if test -f "$deploy_dir/config.server.json"; then
  cp -p "$deploy_dir/config.server.json" "$backup/config.server.json"
python3 - "$deploy_dir/config.server.json" "$enable_agent" <<'PY'
import json
import os
import sys

path = sys.argv[1]
enable_agent = sys.argv[2].lower() == "true"
stat = os.stat(path)
with open(path, "r", encoding="utf-8") as file:
    data = json.load(file)
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
tmp = path + ".new"
with open(tmp, "w", encoding="utf-8") as file:
    json.dump(data, file, ensure_ascii=False, indent=2)
    file.write("\n")
os.chmod(tmp, 0o640)
os.chown(tmp, stat.st_uid, stat.st_gid)
os.replace(tmp, path)
PY
fi

rollback() {
  echo "机器人启动验证失败，开始回滚。" >&2
  for name in $files; do
    if test -f "$backup/$name"; then
      cp -p "$backup/$name" "$deploy_dir/$name"
    else
      rm -f "$deploy_dir/$name"
    fi
  done
  if test -f "$backup/config.server.json"; then
    cp -p "$backup/config.server.json" "$deploy_dir/config.server.json"
  fi
  cd "$deploy_dir"
  docker compose build bug-bot >/dev/null 2>&1 || true
  docker compose up -d --no-deps --force-recreate bug-bot >/dev/null 2>&1 || true
  exit 1
}

cd "$deploy_dir"
docker compose config -q || rollback
docker compose build bug-bot || rollback
docker compose up -d --no-deps --force-recreate bug-bot || rollback
sleep 5
running=$(docker inspect -f '{{.State.Running}}' grandumi-qq-bug-bot 2>/dev/null || true)
test "$running" = "true" || rollback
docker compose exec -T bug-bot python agent_bridge.py status >/tmp/grandumi-agent-bridge-status.txt 2>&1 || rollback
grep -q '"ok": true' /tmp/grandumi-agent-bridge-status.txt || rollback
rm -f /tmp/grandumi-agent-bridge-status.txt
rm -rf "$backup"
echo "BUG_BOT_DEPLOY_OK"
