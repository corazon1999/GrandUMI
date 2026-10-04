#!/usr/bin/env bash
set -Eeuo pipefail

state_dir=/var/lib/grandumi-ha
active_file="$state_dir/active-slot"
failure_file="$state_dir/health-failures"
lock_file="$state_dir/health.lock"
recovery_timeout_seconds="${GRANDUMI_PRODUCTION_RECOVERY_TIMEOUT_SECONDS:-600}"

[[ "$recovery_timeout_seconds" =~ ^[0-9]+$ ]] || {
  logger -t grandumi-health "恢复等待上限必须是整数秒：$recovery_timeout_seconds"
  exit 1
}
(( recovery_timeout_seconds >= 60 && recovery_timeout_seconds <= 600 )) || {
  logger -t grandumi-health "恢复等待上限必须在 60 到 600 秒之间：$recovery_timeout_seconds"
  exit 1
}

mkdir -p "$state_dir"
exec 9>"$lock_file"
flock -n 9 || exit 0

active="$(cat "$active_file" 2>/dev/null || echo a)"
[[ "$active" == a || "$active" == b ]] || active=a
if [[ "$active" == a ]]; then port=8080; else port=8082; fi
unit="grandumi-production-backend@$active.service"

reset_health_state() {
  printf '0\n' > "$failure_file"
}

monotonic_seconds() {
  local uptime_seconds ignored
  read -r uptime_seconds ignored < /proc/uptime || return 1
  uptime_seconds="${uptime_seconds%%.*}"
  [[ "$uptime_seconds" =~ ^[0-9]+$ ]] || return 1
  printf '%s\n' "$uptime_seconds"
}

capture_backend_identity() {
  expected_pid="$(systemctl show "$unit" --property MainPID --value 2>/dev/null || true)"
  expected_invocation="$(systemctl show "$unit" --property InvocationID --value 2>/dev/null || true)"
  [[ "$expected_pid" =~ ^[1-9][0-9]*$ \
      && "$expected_invocation" =~ ^[0-9a-f]{32}$ ]]
}

wait_for_backend_live() {
  local timeout_seconds="$1" context="$2"
  local started_at current_time elapsed=0 next_progress=30
  local current_pid current_invocation

  started_at="$(monotonic_seconds)" || {
    logger -t grandumi-health "无法读取单调时钟，结束槽位 $active 的恢复等待"
    return 1
  }

  while (( elapsed < timeout_seconds )); do
    if curl -fsS --connect-timeout 1 --max-time 2 \
        "http://127.0.0.1:$port/live" >/dev/null 2>&1; then
      reset_health_state
      logger -t grandumi-health \
        "槽位 $active $context 后恢复，等待 ${elapsed} 秒"
      return 0
    fi
    if ! systemctl is-active --quiet "$unit"; then
      logger -t grandumi-health "槽位 $active 在 $context 期间停止运行"
      return 1
    fi
    current_pid="$(systemctl show "$unit" --property MainPID --value 2>/dev/null || true)"
    current_invocation="$(systemctl show "$unit" --property InvocationID --value 2>/dev/null || true)"
    if [[ "$current_pid" != "$expected_pid" \
        || "$current_invocation" != "$expected_invocation" ]]; then
      logger -t grandumi-health \
        "槽位 $active 在 $context 期间发生进程世代变化：$expected_pid -> ${current_pid:-无}"
      return 1
    fi
    if (( elapsed >= next_progress )); then
      logger -t grandumi-health \
        "槽位 $active 仍在 $context，已等待 ${elapsed}/${timeout_seconds} 秒"
      next_progress=$((next_progress + 30))
    fi
    sleep 1
    current_time="$(monotonic_seconds)" || {
      logger -t grandumi-health "无法读取单调时钟，结束槽位 $active 的恢复等待"
      return 1
    }
    if (( current_time < started_at )); then
      logger -t grandumi-health "单调时钟发生回退，结束槽位 $active 的恢复等待"
      return 1
    fi
    elapsed=$((current_time - started_at))
  done

  logger -t grandumi-health \
    "槽位 $active 在 $context 的 ${timeout_seconds} 秒窗口内未恢复"
  return 1
}

attempt_failover() {
  if /usr/local/sbin/grandumi-production-switch --failover; then
    reset_health_state
    logger -t grandumi-health "已自动切换到本机备用槽位"
    exit 0
  fi

  logger -t grandumi-health "本机备用槽位切换失败，保留原槽位并等待下一轮检查"
  exit 1
}

# 自愈使用 /live，而不是会在容量保护触发时返回 503 的 /ready；满载不等于进程故障。
if curl -fsS --max-time 2 "http://127.0.0.1:$port/live" >/dev/null; then
  reset_health_state
  exit 0
fi

failures="$(cat "$failure_file" 2>/dev/null || echo 0)"
[[ "$failures" =~ ^[0-9]+$ ]] || failures=0
failures=$((failures + 1))
printf '%s\n' "$failures" > "$failure_file"
logger -t grandumi-health "槽位 $active 就绪检查失败（$failures/3）"
(( failures >= 3 )) || exit 0

# systemd 可能已经通过 Restart=always 拉起后端。进程仍在启动窗口时先保护当前
# Invocation，避免第三次健康检查再次 restart 并把持久房间恢复打回起点。
active_enter_usec="$(systemctl show "$unit" --property ActiveEnterTimestampMonotonic --value 2>/dev/null || true)"
now_monotonic="$(monotonic_seconds 2>/dev/null || true)"
if systemctl is-active --quiet "$unit" \
    && [[ "$active_enter_usec" =~ ^[0-9]+$ ]] \
    && [[ "$now_monotonic" =~ ^[0-9]+$ ]] \
    && (( active_enter_usec > 0 )) \
    && capture_backend_identity; then
  active_enter_seconds=$((active_enter_usec / 1000000))
  startup_age=$((now_monotonic - active_enter_seconds))
  if (( startup_age >= 0 && startup_age < recovery_timeout_seconds )); then
    remaining_seconds=$((recovery_timeout_seconds - startup_age))
    if wait_for_backend_live "$remaining_seconds" "现有启动恢复"; then
      exit 0
    fi
    attempt_failover
  fi
fi

# 成熟进程失活时只重启原槽一次，并固定 PID 与 InvocationID 等待完整恢复。
# 进程退出、自动重启或十分钟仍未监听都会立即结束等待，再尝试已知备用槽位。
systemctl restart "$unit" || true
if systemctl is-active --quiet "$unit" && capture_backend_identity; then
  if wait_for_backend_live "$recovery_timeout_seconds" "同槽重启恢复"; then
    exit 0
  fi
fi

attempt_failover
