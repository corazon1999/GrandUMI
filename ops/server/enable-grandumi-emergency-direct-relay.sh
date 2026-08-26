#!/usr/bin/env bash
set -euo pipefail

source_config="${1:-/tmp/grandumi-emergency-direct-relay.caddy}"
caddyfile=/etc/caddy/Caddyfile
target=/etc/caddy/conf.d/direct.grand-umi.com.caddy
duplicate=/etc/caddy/conf.d/direct-grandumi-relay.caddy
backup=/etc/caddy/conf.d/direct.grand-umi.com.caddy.pre-relay-20260816
mode_file=/etc/grandumi/primary-domain-mode
lock_file=/run/lock/grandumi-domain-cutover.lock
rendered="/run/grandumi-emergency-direct-relay.$$.caddy"

[[ -f "$source_config" ]] || { echo "缺少中转配置：$source_config" >&2; exit 1; }
[[ -f "$target" ]] || { echo "缺少原直连配置：$target" >&2; exit 1; }
[[ ! -e "$backup" ]] || { echo "回滚备份已存在：$backup" >&2; exit 1; }
command -v flock >/dev/null || { echo "缺少 flock，无法与主域切换互斥" >&2; exit 1; }

install -d -m 0755 /run/lock
trap 'rm -f -- "$rendered"' EXIT
exec 9>"$lock_file"
flock -n 9 || { echo "主域准备、切换或其他中转任务正在执行" >&2; exit 1; }

domain_mode="$(cat "$mode_file" 2>/dev/null || echo legacy)"
case "$domain_mode" in
  legacy) upstream_host=grand-umi.com ;;
  ygo) upstream_host=ygo.grand-umi.com ;;
  *) echo "未知正式主域模式：$domain_mode" >&2; exit 1 ;;
esac

placeholder_count="$( { grep -o '__GRANDUMI_PRIMARY_DOMAIN__' "$source_config" || true; } | wc -l)"
[[ "$placeholder_count" -eq 2 ]] || {
  echo "中转模板必须且只能包含两个正式主域占位符" >&2
  exit 1
}

# 在覆盖直连配置前严格校验所选主域的源站 TLS 与后端就绪状态，防止
# legacy/ygo 模式与证书时序不一致时把 direct 流量送到拒绝站点。
upstream_code="$(curl -sS --max-time 15 \
  --resolve "$upstream_host:443:103.146.230.37" \
  -o /dev/null -w '%{http_code}' "https://$upstream_host/backend/ready")" || {
  echo "所选中转上游 TLS/就绪检查失败：$upstream_host" >&2
  exit 1
}
[[ "$upstream_code" == 200 ]] || {
  echo "所选中转上游未就绪：$upstream_host，HTTP $upstream_code" >&2
  exit 1
}

sed "s/__GRANDUMI_PRIMARY_DOMAIN__/$upstream_host/g" "$source_config" > "$rendered"

cp -a -- "$target" "$backup"
rm -f -- "$duplicate"
install -o root -g root -m 0644 "$rendered" "$target"
rm -f -- "$rendered"

rollback() {
  rm -f -- "$rendered"
  install -o root -g root -m 0644 "$backup" "$target"
  rm -f -- "$duplicate"
  caddy validate --config "$caddyfile" --adapter caddyfile
  systemctl reload caddy
}

if ! caddy validate --config "$caddyfile" --adapter caddyfile; then
  rollback
  exit 1
fi

if ! systemctl reload caddy || ! systemctl is-active --quiet caddy; then
  rollback
  exit 1
fi

rm -f -- "$source_config"
echo "GrandUMI 临时直连中转配置已启用：模式=$domain_mode，上游=$upstream_host。"
