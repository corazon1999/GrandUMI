#!/usr/bin/env bash
set -Eeuo pipefail

repo=/opt/grandumi-test
domain=test.grand-umi.com

die() {
  echo "错误：$*" >&2
  exit 1
}

[[ -d "$repo" ]] || die "测试服仓库不存在：$repo"
systemctl is-active --quiet grandumi-test-backend.service || die "测试服后端未运行"
systemctl is-active --quiet grandumi-test-frontend.service || die "测试服前端未运行"

install -d -m 0755 /var/www/certbot
certbot certonly --webroot --webroot-path /var/www/certbot \
  --domain "$domain" --non-interactive --agree-tos --register-unsafely-without-email \
  --keep-until-expiring

install -m 0644 "$repo/ops/server/grandumi-test.nginx" /etc/nginx/sites-available/grandumi-test
ln -sfn /etc/nginx/sites-available/grandumi-test /etc/nginx/sites-enabled/grandumi-test
nginx -t
systemctl reload nginx
systemctl enable --now certbot.timer

curl -fsS --retry 10 --retry-delay 1 --resolve "$domain:443:127.0.0.1" \
  "https://$domain/backend/ready" >/dev/null
echo "测试服 TLS 已启用：https://$domain"
