#!/usr/bin/env bash
set -Eeuo pipefail

state_dir=/var/lib/grandumi-ha
release_root=/opt/grandumi/releases
slot_root=/opt/grandumi/slots
active_file="$state_dir/active-slot"
standby_file="$state_dir/standby-slot"
lock_file="$state_dir/switch.lock"
mode="${1:-}"
release="${2:-}"
previous_target_backend=""
previous_target_frontend=""

die() { echo "错误：$*" >&2; exit 1; }
verify_qq_access_rollback_compatibility() {
  local target_backend="$1"
  local players_db=/data/grandumi/players.db
  local table_exists initialized marker
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
mkdir -p "$state_dir" "$slot_root/a" "$slot_root/b"
exec 9>"$lock_file"
flock -n 9 || die "另一个切换任务正在执行"

active="$(cat "$active_file" 2>/dev/null || echo a)"
[[ "$active" == a || "$active" == b ]] || active=a
other=b; [[ "$active" == b ]] && other=a

case "$mode" in
  --release)
    [[ "$release" =~ ^[0-9a-f]{40}$ ]] || die "发布切换必须提供 40 位提交号"
    [[ -d "$release_root/$release/backend" && -d "$release_root/$release/frontend" ]] \
      || die "发布包不存在：$release"
    verify_qq_access_rollback_compatibility "$release_root/$release/backend"
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

rollback() {
  systemctl stop "grandumi-production-backend@$target.service" \
    "grandumi-production-frontend@$target.service" || true
  systemctl start "grandumi-production-backend@$active.service" \
    "grandumi-production-frontend@$active.service" || true
  write_proxy "$old_backend_port" "$old_frontend_port" "$active" || true
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
  echo "切换失败，已尝试恢复槽位 $active" >&2
}

trap rollback ERR

# 前端可并行预热；后端受数据目录单写租约保护，必须先停旧后启新。
systemctl start "grandumi-production-frontend@$target.service"
curl -fsS --retry 15 --retry-delay 1 --retry-connrefused \
  "http://127.0.0.1:$frontend_port/" >/dev/null
systemctl stop "grandumi-production-backend@$active.service"
systemctl start "grandumi-production-backend@$target.service"
curl -fsS --retry 25 --retry-delay 1 --retry-connrefused \
  "http://127.0.0.1:$backend_port/ready" >/dev/null
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
echo "正式服已切换：$active -> $target（后端 $backend_port，前端 $frontend_port）"
