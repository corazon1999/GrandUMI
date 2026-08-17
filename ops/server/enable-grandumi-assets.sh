#!/usr/bin/env bash
set -Eeuo pipefail

domain=assets.grand-umi.com
source_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
acme_config="$source_root/ops/server/grandumi-assets-acme.nginx"
tls_config="$source_root/ops/server/grandumi-assets.nginx"

die() { echo "错误：$*" >&2; exit 1; }
[[ -f "$acme_config" && -f "$tls_config" ]] || die "缺少静态资源域名 Nginx 配置"

apt-get -o DPkg::Lock::Timeout=300 update
apt-get -o DPkg::Lock::Timeout=300 install -y --no-install-recommends certbot openssl
install -d -m 0755 /var/www/certbot /etc/nginx/sites-available /etc/nginx/sites-enabled

# 首次迁移时先加载纯 HTTP 验证站点；已有有效证书时可直接保留 HTTPS 服务。
if [[ ! -f "/etc/letsencrypt/live/$domain/fullchain.pem" ]] \
    || ! openssl x509 -in "/etc/letsencrypt/live/$domain/fullchain.pem" \
      -noout -checkhost "$domain" >/dev/null 2>&1; then
  install -m 0644 "$acme_config" /etc/nginx/sites-available/grandumi-assets
  ln -sfn /etc/nginx/sites-available/grandumi-assets /etc/nginx/sites-enabled/grandumi-assets
  nginx -t
  systemctl reload nginx
fi

certbot certonly --webroot --webroot-path /var/www/certbot \
  --domain "$domain" --cert-name "$domain" --non-interactive --agree-tos \
  --register-unsafely-without-email --keep-until-expiring \
  --deploy-hook "systemctl reload nginx"
openssl x509 -in "/etc/letsencrypt/live/$domain/fullchain.pem" \
  -noout -checkhost "$domain" >/dev/null

install -m 0644 "$tls_config" /etc/nginx/sites-available/grandumi-assets
ln -sfn /etc/nginx/sites-available/grandumi-assets /etc/nginx/sites-enabled/grandumi-assets
nginx -t
systemctl reload nginx
systemctl enable --now certbot.timer

curl -fsS --resolve "$domain:443:127.0.0.1" \
  "https://$domain/sprites-thumb/CardBack.webp" >/dev/null
echo "新正式服静态资源域名已启用：https://$domain"
