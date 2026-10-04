#!/usr/bin/env bash
set -Eeuo pipefail

grandumi_assert_production_drained_snapshot() {
  local ready_json="$1"
  local persist_root="$2"
  local context="${3:-正式发布排空门禁}"
  local active_journal

  python3 - "$context" "$ready_json" <<'PY'
import json
import sys

context, ready_json = sys.argv[1:]
try:
    document = json.loads(ready_json)
except Exception as exc:
    raise SystemExit(f"{context}返回了无效 JSON：{exc}")

recovery = document.get("recovery")
if not isinstance(recovery, dict):
    raise SystemExit(f"{context}缺少 recovery 状态")

checks = {
    "maintenance": document.get("maintenance") is True,
    "rooms": type(document.get("rooms")) is int and document.get("rooms") == 0,
    "recovery.pausedRooms": type(recovery.get("pausedRooms")) is int and recovery.get("pausedRooms") == 0,
    "recovery.journalQueueDepth": type(recovery.get("journalQueueDepth")) is int and recovery.get("journalQueueDepth") == 0,
    "recovery.snapshotQueueDepth": type(recovery.get("snapshotQueueDepth")) is int and recovery.get("snapshotQueueDepth") == 0,
}
failed = [name for name, passed in checks.items() if not passed]
if failed:
    raise SystemExit(f"{context}未排空：" + "、".join(failed))

if document.get("status") != "ready" or document.get("storage", {}).get("healthy") is not True:
    raise SystemExit(f"{context}后端或存储未健康就绪")
PY

  [[ -d "$persist_root" ]] || {
    echo "错误：${context}的对局日志目录不存在：$persist_root" >&2
    return 1
  }
  active_journal="$(find "$persist_root" -maxdepth 1 -type f -name '*.jsonl' -print -quit)"
  [[ -z "$active_journal" ]] || {
    echo "错误：${context}仍有活动对局日志：$active_journal" >&2
    return 1
  }
}

grandumi_verify_production_drained_state() {
  local context="${1:-正式发布排空门禁}"
  local active_slot port ready_json

  active_slot="$(tr -d '\r\n' < /var/lib/grandumi-ha/active-slot 2>/dev/null || true)"
  [[ "$active_slot" =~ ^[ab]$ ]] || {
    echo "错误：${context}无法读取有效活动槽位" >&2
    return 1
  }
  port=8080
  [[ "$active_slot" == b ]] && port=8082
  ready_json="$(curl -fsS "http://127.0.0.1:$port/ready")" || {
    echo "错误：${context}无法读取活动后端就绪状态" >&2
    return 1
  }
  grandumi_assert_production_drained_snapshot "$ready_json" /data/grandumi/Persist "$context"
  echo "${context}通过：维护模式已开启，房间、恢复队列与活动日志均为 0。"
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  case "${1:-}" in
    --snapshot)
      [[ $# == 4 ]] || {
        echo "用法：grandumi-production-drained-state.sh --snapshot <ready.json> <Persist目录> <说明>" >&2
        exit 1
      }
      grandumi_assert_production_drained_snapshot "$(cat -- "$2")" "$3" "$4"
      ;;
    --live)
      [[ $# -le 2 ]] || {
        echo "用法：grandumi-production-drained-state.sh --live [说明]" >&2
        exit 1
      }
      grandumi_verify_production_drained_state "${2:-正式发布排空门禁}"
      ;;
    *)
      echo "用法：grandumi-production-drained-state.sh --live [说明] | --snapshot <ready.json> <Persist目录> <说明>" >&2
      exit 1
      ;;
  esac
fi
