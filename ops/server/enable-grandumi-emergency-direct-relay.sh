#!/usr/bin/env bash
set -euo pipefail

source_config="${1:-/tmp/grandumi-emergency-direct-relay.caddy}"
caddyfile=/etc/caddy/Caddyfile
target=/etc/caddy/conf.d/direct.grand-umi.com.caddy
duplicate=/etc/caddy/conf.d/direct-grandumi-relay.caddy
backup=/etc/caddy/conf.d/direct.grand-umi.com.caddy.pre-relay-20260816

[[ -f "$source_config" ]] || { echo "缺少中转配置：$source_config" >&2; exit 1; }
[[ -f "$target" ]] || { echo "缺少原直连配置：$target" >&2; exit 1; }
[[ ! -e "$backup" ]] || { echo "回滚备份已存在：$backup" >&2; exit 1; }

cp -a -- "$target" "$backup"
rm -f -- "$duplicate"
install -o root -g root -m 0644 "$source_config" "$target"

rollback() {
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
echo "GrandUMI 临时直连中转配置已启用。"
