#!/usr/bin/env bash
set -Eeuo pipefail

interface="${GRANDUMI_NETWORK_INTERFACE:-$(ip -4 route show default | awk 'NR == 1 { print $5 }')}"
rate="${GRANDUMI_EGRESS_RATE:-160mbit}"

[[ -n "$interface" ]] || {
  echo "错误：无法识别默认公网网卡。" >&2
  exit 1
}

modprobe tcp_bbr
modprobe sch_fq
modprobe sch_htb

# 阿里云公网出口出现拥塞时，先在本机平滑发送，避免运营商队列大量丢包后让 TCP 反复退避。
tc qdisc del dev "$interface" root 2>/dev/null || true
tc qdisc add dev "$interface" root handle 1: htb default 10
tc class add dev "$interface" parent 1: classid 1:10 \
  htb rate "$rate" ceil "$rate" burst 256k cburst 256k quantum 15140
tc qdisc add dev "$interface" parent 1:10 handle 10: fq

echo "GrandUMI 出口整形已启用：网卡=$interface，速率=$rate"
