#!/usr/bin/env bash
set -Eeuo pipefail

repo=/opt/grandumi
production_ip="${GRANDUMI_PRODUCTION_IP:-103.146.230.37}"

[[ "$production_ip" == "103.146.230.37" ]] || { echo "拒绝在未登记主机上初始化：$production_ip" >&2; exit 1; }
[[ -f /etc/letsencrypt/live/grand-umi.com/fullchain.pem ]] || { echo "缺少 grand-umi.com 证书" >&2; exit 1; }

id grandumi >/dev/null 2>&1 || useradd --system --home /nonexistent --shell /usr/sbin/nologin grandumi
install -d -o grandumi -g grandumi -m 0750 /data/grandumi
install -d -m 0755 /etc/nginx/snippets /var/www/certbot

install -m 0644 "$repo/ops/server/grandumi-production-backend.service" /etc/systemd/system/grandumi-production-backend.service
install -m 0644 "$repo/ops/server/grandumi-production-frontend.service" /etc/systemd/system/grandumi-production-frontend.service
install -m 0644 "$repo/ops/server/grandumi-production-proxy.nginx" /etc/nginx/snippets/grandumi-production-proxy.conf
install -m 0644 "$repo/ops/server/grandumi-production.nginx" /etc/nginx/sites-available/grandumi-production
ln -sfn /etc/nginx/sites-available/grandumi-production /etc/nginx/sites-enabled/grandumi-production
rm -f /etc/nginx/sites-enabled/default

systemctl daemon-reload
nginx -t
systemctl reload nginx

curl -kfsS --resolve grand-umi.com:443:127.0.0.1 https://grand-umi.com/backend/ready >/dev/null
echo "新正式服主域名入口已预置：IP=$production_ip"
