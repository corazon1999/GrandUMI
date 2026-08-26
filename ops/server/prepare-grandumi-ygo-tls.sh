#!/usr/bin/env bash
set -Eeuo pipefail

domain=ygo.grand-umi.com
source_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
if [[ -f "$source_root/ops/server/grandumi-ygo-acme.nginx" ]]; then
  acme_config="$source_root/ops/server/grandumi-ygo-acme.nginx"
  precut_config="$source_root/ops/server/grandumi-ygo-precut.nginx"
else
  acme_config=/etc/nginx/sites-available/grandumi-ygo-acme-template
  precut_config=/etc/nginx/sites-available/grandumi-ygo-precut-template
fi
site_available=/etc/nginx/sites-available/grandumi-ygo-precut
site_enabled=/etc/nginx/sites-enabled/grandumi-ygo-precut
challenge_root=/var/www/certbot

die() { echo "错误：$*" >&2; exit 1; }
[[ "$EUID" -eq 0 ]] || die "必须以 root 执行"
[[ -f "$acme_config" && -f "$precut_config" ]] || die "缺少 ygo 证书准备配置"
[[ -f /etc/letsencrypt/live/grand-umi.com/fullchain.pem ]] || die "缺少旧主域证书，无法安全隔离预切域名"

install -d -m 0755 /run/lock /etc/nginx/sites-available /etc/nginx/sites-enabled "$challenge_root"
exec 9>/run/lock/grandumi-domain-cutover.lock
flock -n 9 || die "已有域名准备或切换任务正在执行"

apt-get -o DPkg::Lock::Timeout=300 update
apt-get -o DPkg::Lock::Timeout=300 install -y --no-install-recommends certbot openssl

# 先用旧证书承载一个只返回 503 的隔离站点。即使 Cloudflare 使用非严格
# Full 模式，新域名也不会在正式切换前落入当前 default_server。
install -m 0644 "$acme_config" "$site_available"
ln -sfn "$site_available" "$site_enabled"
nginx -t
systemctl reload nginx

mapfile -t resolved_ipv4 < <(getent ahostsv4 "$domain" | awk '{ print $1 }' | sort -u)
[[ "${#resolved_ipv4[@]}" -gt 0 ]] || die "$domain 尚无公开 A 记录；请先在 Cloudflare 新增代理记录"

challenge_name="grandumi-preflight-$(date -u +%Y%m%d%H%M%S)-$$"
challenge_dir="$challenge_root/.well-known/acme-challenge"
challenge_file="$challenge_dir/$challenge_name"
install -d -m 0755 "$challenge_dir"
printf '%s\n' "$challenge_name" > "$challenge_file"
cleanup_challenge() { rm -f "$challenge_file"; }
trap cleanup_challenge EXIT
challenge_response="$(curl -fsS --max-time 15 "http://$domain/.well-known/acme-challenge/$challenge_name")" \
  || die "HTTP-01 预检失败；请确认 ygo DNS 指向本机且 Cloudflare 未拦截验证路径"
[[ "$challenge_response" == "$challenge_name" ]] || die "HTTP-01 预检内容不匹配，拒绝签发证书"

certbot certonly --webroot --webroot-path "$challenge_root" \
  --domain "$domain" --cert-name "$domain" --non-interactive --agree-tos \
  --register-unsafely-without-email --keep-until-expiring \
  --deploy-hook "systemctl reload nginx"
openssl x509 -in "/etc/letsencrypt/live/$domain/fullchain.pem" \
  -noout -checkhost "$domain" >/dev/null

# 换成主机名匹配的证书，但仍保持 503 隔离；真正开放只能由停机切换脚本完成。
install -m 0644 "$precut_config" "$site_available"
nginx -t
systemctl reload nginx
systemctl enable --now certbot.timer

strict_code="$(curl -sS --max-time 15 --resolve "$domain:443:127.0.0.1" \
  -o /dev/null -w '%{http_code}' "https://$domain/")"
[[ "$strict_code" == 503 ]] || die "预切隔离验证失败：HTTPS $strict_code"
echo "$domain 证书已就绪，预切入口保持 HTTP 503；尚未切换正式流量。"
