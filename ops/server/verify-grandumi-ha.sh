#!/usr/bin/env bash
set -Eeuo pipefail

mode="${1:---check}"
state_dir=/var/lib/grandumi-ha
active="$(cat "$state_dir/active-slot" 2>/dev/null || echo a)"
standby="$(cat "$state_dir/standby-slot" 2>/dev/null || true)"
[[ "$active" == a || "$active" == b ]] || { echo "活动槽位记录无效" >&2; exit 1; }
port=8080; [[ "$active" == b ]] && port=8082

ready="$(curl -fsS --max-time 3 "http://127.0.0.1:$port/ready")"
version="$(curl -fsS --max-time 3 "http://127.0.0.1:$port/version")"
systemctl is-active --quiet "grandumi-production-backend@$active.service"
systemctl is-active --quiet "grandumi-production-frontend@$active.service"
systemctl is-active --quiet grandumi-production-health.timer
nginx -t >/dev/null

echo "活动槽位：$active；备用槽位：${standby:-未建立}"
echo "就绪状态：$ready"
echo "版本信息：$version"

[[ "$mode" == --check ]] && exit 0
[[ "$mode" == --switch-drill ]] || { echo "用法：verify-grandumi-ha [--check|--switch-drill]" >&2; exit 2; }
[[ "$standby" == a || "$standby" == b ]] || { echo "没有可演练的备用槽位" >&2; exit 1; }

rooms="$(python3 -c 'import json,sys; print(json.load(sys.stdin).get("rooms", -1))' <<<"$ready")"
[[ "$rooms" == 0 ]] || { echo "当前仍有 $rooms 个房间，拒绝故障切换演练" >&2; exit 1; }

/usr/local/sbin/grandumi-production-switch --failover
new_active="$(cat "$state_dir/active-slot")"
[[ "$new_active" != "$active" ]] || { echo "演练未发生槽位切换" >&2; exit 1; }
echo "无房间故障切换演练通过：$active -> $new_active"
