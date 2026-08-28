#!/usr/bin/env bash
set -uo pipefail

# 保留锁定镜像的官方 entrypoint 原样运行；本脚本只负责 PID 1 信号转发。
readonly official_entrypoint=/app/entrypoint.sh
readonly stop_timeout="${GRANDUMI_NAPCAT_STOP_TIMEOUT_SECONDS:-20}"
readonly quick_password_secret=/run/secrets/napcat_quick_password_md5

if [[ ! "$stop_timeout" =~ ^[0-9]+$ ]] \
  || (( stop_timeout < 1 || stop_timeout > 25 )); then
  echo "NapCat 停止等待秒数必须是 1 到 25 的整数。" >&2
  exit 64
fi
if [[ ! -f "$official_entrypoint" ]]; then
  echo "找不到锁定镜像的 NapCat 官方入口。" >&2
  exit 66
fi

# 摘要只通过 Docker secret 进入容器，避免出现在 Compose 环境和 docker inspect 中。
if [[ -r "$quick_password_secret" ]]; then
  quick_password_md5="$(tr -d '\r\n' < "$quick_password_secret")"
  if [[ ! "$quick_password_md5" =~ ^[a-fA-F0-9]{32}$ ]]; then
    echo "NapCat 私有密码摘要必须是 32 位 MD5 十六进制。" >&2
    exit 65
  fi
  export NAPCAT_QUICK_PASSWORD_MD5="${quick_password_md5,,}"
  unset quick_password_md5
  echo "已加载 NapCat 私有密码摘要回退。"
fi

child_pid=""
stopping=0

process_group_alive() {
  [[ -n "$child_pid" ]] && kill -0 -- "-$child_pid" 2>/dev/null
}

stop_process_group() {
  if (( stopping )); then
    return
  fi
  stopping=1
  trap '' TERM INT HUP

  echo "收到停止信号，正在通知 NapCat/QQ 安全退出……"
  kill -TERM -- "-$child_pid" 2>/dev/null || true
  local deadline=$((SECONDS + stop_timeout))
  while process_group_alive && (( SECONDS < deadline )); do
    sleep 0.2
  done
  if process_group_alive; then
    echo "NapCat/QQ 未在 ${stop_timeout} 秒内退出，执行有界兜底清理。" >&2
    kill -KILL -- "-$child_pid" 2>/dev/null || true
  fi
  wait "$child_pid" 2>/dev/null || true
  exit 0
}

# 独立进程组确保官方入口、QQ 与 Xvfb 同时收到停止信号。
setsid /bin/bash "$official_entrypoint" "$@" &
child_pid=$!
trap stop_process_group TERM INT HUP

set +e
wait "$child_pid"
status=$?
set -e
if (( stopping )); then
  exit 0
fi
exit "$status"
