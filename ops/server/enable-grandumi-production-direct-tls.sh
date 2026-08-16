#!/usr/bin/env bash
set -Eeuo pipefail

domain=direct.grand-umi.com
production_ip=103.146.230.37
source_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
compat_source="$source_root/ops/server/isrg-root-x2-cross-signed.pem"
renew_hook_source="$source_root/ops/server/renew-grandumi-direct-certificate.sh"

mapfile -t resolved_ipv4 < <(getent ahostsv4 "$domain" | awk '{ print $1 }' | sort -u)
printf '%s\n' "${resolved_ipv4[@]}" | grep -Fxq "$production_ip" || {
  echo "拒绝签发证书：$domain 尚未解析到新正式服 $production_ip" >&2
  exit 1
}
[[ "${#resolved_ipv4[@]}" -eq 1 ]] || {
  echo "拒绝启用直连：$domain 仍解析到其他 IPv4：${resolved_ipv4[*]}" >&2
  exit 1
}

apt-get -o DPkg::Lock::Timeout=300 update
apt-get -o DPkg::Lock::Timeout=300 install -y --no-install-recommends certbot openssl
install -d -m 0755 /var/www/certbot /etc/nginx/snippets
install -d -m 0755 /etc/letsencrypt/compat /etc/letsencrypt/renewal-hooks/deploy
install -m 0644 "$compat_source" /etc/letsencrypt/compat/isrg-root-x2-cross-signed.pem
install -m 0755 "$renew_hook_source" \
  /etc/letsencrypt/renewal-hooks/deploy/grandumi-direct-certificate

# 当前主域名 default_server 已开放同一 webroot；DNS 切换后可直接完成 HTTP-01，
# 不需要先加载引用尚不存在证书的直连 HTTPS 站点。
certbot certonly --webroot --webroot-path /var/www/certbot \
  --domain "$domain" --non-interactive --agree-tos --register-unsafely-without-email \
  --key-type rsa --rsa-key-size 2048 --keep-until-expiring
/etc/letsencrypt/renewal-hooks/deploy/grandumi-direct-certificate
openssl x509 -in "/etc/letsencrypt/live/$domain/fullchain.pem" \
  -noout -checkhost "$domain" >/dev/null

install -m 0644 "$source_root/ops/server/grandumi-production-proxy.nginx" \
  /etc/nginx/snippets/grandumi-production-proxy.conf
install -m 0644 "$source_root/ops/server/grandumi-production.nginx" \
  /etc/nginx/sites-available/grandumi-production
ln -sfn /etc/nginx/sites-available/grandumi-production \
  /etc/nginx/sites-enabled/grandumi-production
nginx -t
systemctl reload nginx

active_slot="$(cat /var/lib/grandumi-ha/active-slot 2>/dev/null || echo a)"
runtime_config="/opt/grandumi/slots/$active_slot/frontend/public/network-endpoints.json"
[[ -d "$(dirname "$runtime_config")" ]] || {
  echo "活动前端槽不存在：$active_slot" >&2
  exit 1
}
cat > "$runtime_config.next" <<'JSON'
{"version":1,"hosts":["grand-umi.com","direct.grand-umi.com"],"endpoints":[{"url":"wss://direct.grand-umi.com/ws","enabled":true},{"url":"wss://grand-umi.com/ws","enabled":true}]}
JSON
mv "$runtime_config.next" "$runtime_config"

systemctl enable --now certbot.timer
rm -f /etc/letsencrypt/renewal-hooks/deploy/reload-nginx

curl -fsS --resolve "$domain:443:127.0.0.1" \
  "https://$domain/backend/ready" >/dev/null
echo "新正式服低延迟直连已启用：https://$domain，WebSocket=wss://$domain/ws"
