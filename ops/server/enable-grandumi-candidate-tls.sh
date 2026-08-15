#!/usr/bin/env bash
set -Eeuo pipefail

domain=candidate.grand-umi.com
candidate_ip=103.146.230.37
repo=/opt/grandumi-candidate

resolved="$(getent ahostsv4 "$domain" | awk 'NR == 1 { print $1 }')"
[[ "$resolved" == "$candidate_ip" ]] || {
  echo "拒绝签发证书：$domain 当前解析为 ${resolved:-无}，期望 $candidate_ip" >&2
  exit 1
}

apt-get -o DPkg::Lock::Timeout=300 update
apt-get -o DPkg::Lock::Timeout=300 install -y --no-install-recommends certbot
install -d -m 0755 /var/www/certbot
install -m 0644 "$repo/ops/server/grandumi-candidate-acme.nginx" /etc/nginx/sites-available/grandumi-candidate
nginx -t
systemctl reload nginx

certbot certonly --webroot --webroot-path /var/www/certbot \
  --domain "$domain" --non-interactive --agree-tos --register-unsafely-without-email \
  --keep-until-expiring

install -m 0644 "$repo/ops/server/grandumi-candidate-tls.nginx" /etc/nginx/sites-available/grandumi-candidate
cat > "$repo/opcgpro-web/public/network-endpoints.json" <<JSON
{"version":1,"hosts":["$domain"],"endpoints":[{"url":"wss://$domain/ws","enabled":true}]}
JSON
nginx -t
systemctl reload nginx
systemctl enable --now certbot.timer
install -d -m 0755 /etc/letsencrypt/renewal-hooks/deploy
printf '#!/usr/bin/env bash\nset -e\nsystemctl reload nginx\n' \
  > /etc/letsencrypt/renewal-hooks/deploy/reload-nginx
chmod 0755 /etc/letsencrypt/renewal-hooks/deploy/reload-nginx

curl -fsS --retry 10 --retry-delay 1 "https://$domain/backend/ready" >/dev/null
echo "候选域名 TLS 已启用：https://$domain，WebSocket=wss://$domain/ws"
