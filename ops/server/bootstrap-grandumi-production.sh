#!/usr/bin/env bash
set -Eeuo pipefail

repo=/opt/grandumi
source_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
production_ip="${GRANDUMI_PRODUCTION_IP:-103.146.230.37}"

[[ "$production_ip" == "103.146.230.37" ]] || { echo "拒绝在未登记主机上初始化：$production_ip" >&2; exit 1; }
[[ -f /etc/letsencrypt/live/grand-umi.com/fullchain.pem ]] || { echo "缺少 grand-umi.com 证书" >&2; exit 1; }
[[ -f /etc/letsencrypt/live/direct.grand-umi.com/fullchain.pem ]] || {
  echo "缺少 direct.grand-umi.com 证书；必须先迁移低延迟直连域名" >&2
  exit 1
}
openssl x509 -in /etc/letsencrypt/live/direct.grand-umi.com/fullchain.pem \
  -noout -checkhost direct.grand-umi.com >/dev/null \
  || { echo "direct.grand-umi.com 证书主机名校验失败" >&2; exit 1; }

id grandumi >/dev/null 2>&1 || useradd --system --home /nonexistent --shell /usr/sbin/nologin grandumi
install -d -o grandumi -g grandumi -m 0750 /data/grandumi
install -d -m 0755 /etc/nginx/snippets /var/www/certbot /etc/grandumi
install -d -o grandumi -g grandumi -m 0755 /opt/grandumi/releases /opt/grandumi/slots/a /opt/grandumi/slots/b
install -d -m 0755 /var/lib/grandumi-ha

cat > /etc/grandumi/backend-a.env <<'EOF'
GRANDUMI_BACKEND_PORT=8080
EOF
cat > /etc/grandumi/backend-b.env <<'EOF'
GRANDUMI_BACKEND_PORT=8082
EOF
cat > /etc/grandumi/frontend-a.env <<'EOF'
GRANDUMI_FRONTEND_PORT=3000
EOF
cat > /etc/grandumi/frontend-b.env <<'EOF'
GRANDUMI_FRONTEND_PORT=3002
EOF

install -m 0644 "$source_root/ops/server/grandumi-production-backend.service" /etc/systemd/system/grandumi-production-backend.service
install -m 0644 "$source_root/ops/server/grandumi-production-frontend.service" /etc/systemd/system/grandumi-production-frontend.service
install -m 0644 "$source_root/ops/server/grandumi-production.slice" /etc/systemd/system/grandumi-production.slice
install -m 0644 "$source_root/ops/server/grandumi-build.slice" /etc/systemd/system/grandumi-build.slice
install -m 0644 "$source_root/ops/server/grandumi-production-backend@.service" /etc/systemd/system/grandumi-production-backend@.service
install -m 0644 "$source_root/ops/server/grandumi-production-frontend@.service" /etc/systemd/system/grandumi-production-frontend@.service
install -m 0755 "$source_root/ops/server/grandumi-production-switch.sh" /usr/local/sbin/grandumi-production-switch
install -m 0755 "$source_root/ops/server/grandumi-production-health-check.sh" /usr/local/sbin/grandumi-production-health-check
install -m 0755 "$source_root/ops/server/verify-grandumi-ha.sh" /usr/local/sbin/verify-grandumi-ha
install -m 0755 "$source_root/ops/server/enable-grandumi-assets.sh" /usr/local/sbin/enable-grandumi-assets
install -m 0644 "$source_root/ops/server/grandumi-production-health.service" /etc/systemd/system/grandumi-production-health.service
install -m 0644 "$source_root/ops/server/grandumi-production-health.timer" /etc/systemd/system/grandumi-production-health.timer
install -m 0644 "$source_root/ops/server/grandumi-production-proxy.nginx" /etc/nginx/snippets/grandumi-production-proxy.conf
install -m 0644 "$source_root/ops/server/grandumi-production.nginx" /etc/nginx/sites-available/grandumi-production
install -m 0644 "$source_root/ops/server/grandumi-assets-acme.nginx" /etc/nginx/sites-available/grandumi-assets-acme
install -m 0644 "$source_root/ops/server/grandumi-assets.nginx" /etc/nginx/sites-available/grandumi-assets
ln -sfn /etc/nginx/sites-available/grandumi-production /etc/nginx/sites-enabled/grandumi-production
if [[ -f /etc/letsencrypt/live/assets.grand-umi.com/fullchain.pem ]] \
    && openssl x509 -in /etc/letsencrypt/live/assets.grand-umi.com/fullchain.pem \
      -noout -checkhost assets.grand-umi.com >/dev/null 2>&1; then
  ln -sfn /etc/nginx/sites-available/grandumi-assets /etc/nginx/sites-enabled/grandumi-assets
fi
rm -f /etc/nginx/sites-enabled/default

[[ -f /etc/nginx/snippets/grandumi-active-backend.conf ]] || \
  printf 'proxy_pass http://127.0.0.1:8080;\n' > /etc/nginx/snippets/grandumi-active-backend.conf
[[ -f /etc/nginx/snippets/grandumi-active-frontend.conf ]] || \
  printf 'proxy_pass http://127.0.0.1:3000;\n' > /etc/nginx/snippets/grandumi-active-frontend.conf
[[ -f /etc/nginx/snippets/grandumi-active-assets.conf ]] || \
  printf 'root /opt/grandumi/opcgpro-web/public;\n' > /etc/nginx/snippets/grandumi-active-assets.conf
[[ -f /etc/nginx/snippets/grandumi-active-frontend-files.conf ]] || \
  printf 'root /opt/grandumi/slots/a/frontend;\n' > /etc/nginx/snippets/grandumi-active-frontend-files.conf
[[ -s /var/lib/grandumi-ha/active-slot ]] || printf 'a\n' > /var/lib/grandumi-ha/active-slot

systemctl daemon-reload
systemctl enable --now grandumi-production-health.timer
nginx -t
systemctl reload nginx

curl -kfsS --resolve grand-umi.com:443:127.0.0.1 https://grand-umi.com/backend/ready >/dev/null
curl -fsS --resolve direct.grand-umi.com:443:127.0.0.1 \
  https://direct.grand-umi.com/backend/ready >/dev/null
echo "新正式服主域名与低延迟直连入口已预置：IP=$production_ip"
