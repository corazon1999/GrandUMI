#!/usr/bin/env bash
set -Eeuo pipefail

state_dir=/var/lib/grandumi-ha
active_file="$state_dir/active-slot"
failure_file="$state_dir/health-failures"
lock_file="$state_dir/health.lock"

mkdir -p "$state_dir"
exec 9>"$lock_file"
flock -n 9 || exit 0

active="$(cat "$active_file" 2>/dev/null || echo a)"
[[ "$active" == a || "$active" == b ]] || active=a
if [[ "$active" == a ]]; then port=8080; else port=8082; fi

# 自愈使用 /live，而不是会在容量保护触发时返回 503 的 /ready；满载不等于进程故障。
if curl -fsS --max-time 2 "http://127.0.0.1:$port/live" >/dev/null; then
  printf '0\n' > "$failure_file"
  exit 0
fi

failures="$(cat "$failure_file" 2>/dev/null || echo 0)"
[[ "$failures" =~ ^[0-9]+$ ]] || failures=0
failures=$((failures + 1))
printf '%s\n' "$failures" > "$failure_file"
logger -t grandumi-health "槽位 $active 就绪检查失败（$failures/3）"
(( failures >= 3 )) || exit 0

# 先给同槽位一次快速自愈机会；普通进程崩溃通常已被 systemd Restart=always 拉起。
systemctl restart "grandumi-production-backend@$active.service" || true
for _ in {1..8}; do
  sleep 1
  if curl -fsS --max-time 2 "http://127.0.0.1:$port/live" >/dev/null; then
    printf '0\n' > "$failure_file"
    logger -t grandumi-health "槽位 $active 重启后恢复"
    exit 0
  fi
done

# 同槽位无法恢复才切到上一个已知可用槽位；切换脚本自身负责失败回滚。
if /usr/local/sbin/grandumi-production-switch --failover; then
  printf '0\n' > "$failure_file"
  logger -t grandumi-health "已自动切换到本机备用槽位"
  exit 0
fi

logger -t grandumi-health "本机备用槽位切换失败，保留原槽位并等待下一轮检查"
exit 1
