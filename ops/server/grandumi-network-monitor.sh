#!/usr/bin/env bash
set -Eeuo pipefail

export LC_ALL=C

interface="${GRANDUMI_NETWORK_INTERFACE:-$(ip -4 route show default | awk 'NR == 1 { print $5 }')}"
metrics_url="${GRANDUMI_METRICS_URL:-http://127.0.0.1:8080/metrics}"
state_file="${GRANDUMI_NETWORK_MONITOR_STATE:-/var/lib/grandumi-network-monitor/last-sample}"

[[ -n "$interface" ]] || {
  echo "错误：无法识别默认公网网卡。" >&2
  exit 1
}

number_or_zero() {
  if [[ "${1:-}" =~ ^[0-9]+$ ]]; then
    printf '%s' "$1"
  else
    printf '0'
  fi
}

counter_delta() {
  local current="$1"
  local previous="$2"
  if (( current >= previous )); then
    printf '%s' "$((current - previous))"
  else
    # qdisc 或主机重启后累计计数会归零，此时从当前值重新开始。
    printf '%s' "$current"
  fi
}

now_epoch="$(date +%s)"
timestamp="$(date --iso-8601=seconds)"

tc_line="$(tc -s class show dev "$interface" | awk '/^ Sent / { print; exit }')"
tc_bytes="$(number_or_zero "$(awk '{ print $2 }' <<<"$tc_line")")"
tc_packets="$(number_or_zero "$(awk '{ print $4 }' <<<"$tc_line")")"
tc_drops="$(number_or_zero "$(sed -n 's/.*dropped \([0-9][0-9]*\).*/\1/p' <<<"$tc_line")")"
tc_overlimits="$(number_or_zero "$(sed -n 's/.*overlimits \([0-9][0-9]*\).*/\1/p' <<<"$tc_line")")"

nstat_data="$(nstat -az 2>/dev/null || true)"
nstat_value() {
  local name="$1"
  awk -v wanted="$name" '$1 == wanted { print $2; found=1; exit } END { if (!found) print 0 }' <<<"$nstat_data"
}

tcp_retrans="$(number_or_zero "$(nstat_value TcpRetransSegs)")"
tcp_timeouts="$(number_or_zero "$(nstat_value TcpExtTCPTimeouts)")"
tcp_abort_timeouts="$(number_or_zero "$(nstat_value TcpExtTCPAbortOnTimeout)")"
net_tx_drops="$(number_or_zero "$(<"/sys/class/net/$interface/statistics/tx_dropped")")"
net_rx_drops="$(number_or_zero "$(<"/sys/class/net/$interface/statistics/rx_dropped")")"

app_metrics="$(curl -fsS --max-time 3 "$metrics_url" 2>/dev/null || true)"
metric_value() {
  local name="$1"
  awk -v wanted="$name" '$1 == wanted { print $2; found=1; exit } END { if (!found) print -1 }' <<<"$app_metrics"
}

connections="$(metric_value grandumi_connections)"
players="$(metric_value grandumi_logged_in_players)"
rooms="$(metric_value grandumi_rooms)"
app_drops="$(metric_value grandumi_websocket_dropped_messages_total)"
overloaded="$(metric_value grandumi_overloaded)"

previous_epoch="$now_epoch"
previous_bytes="$tc_bytes"
previous_drops="$tc_drops"
previous_overlimits="$tc_overlimits"
previous_retrans="$tcp_retrans"
previous_timeouts="$tcp_timeouts"
previous_abort_timeouts="$tcp_abort_timeouts"
interval_seconds=0

if [[ -r "$state_file" ]]; then
  read -r previous_epoch previous_bytes previous_drops previous_overlimits \
    previous_retrans previous_timeouts previous_abort_timeouts < "$state_file" || true
  previous_epoch="$(number_or_zero "$previous_epoch")"
  previous_bytes="$(number_or_zero "$previous_bytes")"
  previous_drops="$(number_or_zero "$previous_drops")"
  previous_overlimits="$(number_or_zero "$previous_overlimits")"
  previous_retrans="$(number_or_zero "$previous_retrans")"
  previous_timeouts="$(number_or_zero "$previous_timeouts")"
  previous_abort_timeouts="$(number_or_zero "$previous_abort_timeouts")"
  if (( now_epoch > previous_epoch )); then
    interval_seconds="$((now_epoch - previous_epoch))"
  fi
fi

bytes_delta="$(counter_delta "$tc_bytes" "$previous_bytes")"
drops_delta="$(counter_delta "$tc_drops" "$previous_drops")"
overlimits_delta="$(counter_delta "$tc_overlimits" "$previous_overlimits")"
retrans_delta="$(counter_delta "$tcp_retrans" "$previous_retrans")"
timeouts_delta="$(counter_delta "$tcp_timeouts" "$previous_timeouts")"
abort_timeouts_delta="$(counter_delta "$tcp_abort_timeouts" "$previous_abort_timeouts")"

egress_mbps="0.000"
if (( interval_seconds > 0 )); then
  egress_mbps="$(awk -v bytes="$bytes_delta" -v seconds="$interval_seconds" \
    'BEGIN { printf "%.3f", bytes * 8 / seconds / 1000000 }')"
fi

install -d -m 0750 "$(dirname "$state_file")"
printf '%s %s %s %s %s %s %s\n' \
  "$now_epoch" "$tc_bytes" "$tc_drops" "$tc_overlimits" \
  "$tcp_retrans" "$tcp_timeouts" "$tcp_abort_timeouts" > "$state_file.next"
mv -f "$state_file.next" "$state_file"

printf '{"timestamp":"%s","interface":"%s","interval_seconds":%s,' \
  "$timestamp" "$interface" "$interval_seconds"
printf '"egress_mbps":%s,"egress_bytes_delta":%s,"tc_bytes_total":%s,"tc_packets_total":%s,' \
  "$egress_mbps" "$bytes_delta" "$tc_bytes" "$tc_packets"
printf '"tc_drops_delta":%s,"tc_drops_total":%s,"tc_overlimits_delta":%s,"tc_overlimits_total":%s,' \
  "$drops_delta" "$tc_drops" "$overlimits_delta" "$tc_overlimits"
printf '"tcp_retrans_delta":%s,"tcp_timeouts_delta":%s,"tcp_abort_timeouts_delta":%s,' \
  "$retrans_delta" "$timeouts_delta" "$abort_timeouts_delta"
printf '"net_tx_drops_total":%s,"net_rx_drops_total":%s,' "$net_tx_drops" "$net_rx_drops"
printf '"connections":%s,"players":%s,"rooms":%s,"app_drops_total":%s,"overloaded":%s}\n' \
  "$connections" "$players" "$rooms" "$app_drops" "$overloaded"
