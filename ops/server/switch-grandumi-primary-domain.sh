#!/usr/bin/env bash
set -Eeuo pipefail

action="${1:-}"
source_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
if [[ -f "$source_root/ops/server/grandumi-production.nginx" ]]; then
  legacy_config="$source_root/ops/server/grandumi-production.nginx"
  ygo_config="$source_root/ops/server/grandumi-production-ygo.nginx"
  precut_config="$source_root/ops/server/grandumi-ygo-precut.nginx"
else
  legacy_config=/etc/nginx/sites-available/grandumi-production-legacy
  ygo_config=/etc/nginx/sites-available/grandumi-production-ygo
  precut_config=/etc/nginx/sites-available/grandumi-ygo-precut-template
fi
live_config=/etc/nginx/sites-available/grandumi-production
live_site=/etc/nginx/sites-enabled/grandumi-production
precut_site_available=/etc/nginx/sites-available/grandumi-ygo-precut
precut_site_enabled=/etc/nginx/sites-enabled/grandumi-ygo-precut
mode_file=/etc/grandumi/primary-domain-mode
state_root=/var/lib/grandumi-domain-cutover
old_domain=grand-umi.com
new_domain=ygo.grand-umi.com
direct_domain=direct.grand-umi.com

die() { echo "错误：$*" >&2; exit 1; }
[[ "$action" == cutover || "$action" == rollback ]] \
  || die "用法：$0 cutover|rollback"
[[ "$EUID" -eq 0 ]] || die "必须以 root 执行"
[[ -f "$legacy_config" && -f "$ygo_config" && -f "$precut_config" ]] \
  || die "缺少域名切换 Nginx 配置"
[[ -f "$live_config" ]] || die "缺少当前正式站点配置：$live_config"
command -v flock >/dev/null || die "缺少 flock，无法建立切换互斥锁"
command -v ss >/dev/null || die "缺少 ss，无法核验监听端口"

install -d -m 0755 /run/lock /etc/grandumi "$state_root" \
  /etc/nginx/sites-available /etc/nginx/sites-enabled
exec 9>/run/lock/grandumi-domain-cutover.lock
flock -n 9 || die "已有域名准备或切换任务正在执行"

# 域名切换会改变所有新连接入口。无论切换还是回退，都必须先停止所有
# 正式后端实例，禁止在仍有连接、房间或持久化写入时改入口。
production_units=(
  grandumi-production-backend.service
  grandumi-production-backend@a.service
  grandumi-production-backend@b.service
)
for unit in "${production_units[@]}"; do
  if systemctl is-active --quiet "$unit"; then
    die "正式后端仍在运行：$unit；请先完成维护排空并停服"
  fi
done
if ss -Hlnpt | grep -Eq ':(8080|8082)([[:space:]]|$)'; then
  die "正式后端端口 8080/8082 仍在监听；拒绝切换"
fi

for domain in "$old_domain" "$direct_domain"; do
  [[ -f "/etc/letsencrypt/live/$domain/fullchain.pem" ]] || die "缺少证书：$domain"
  openssl x509 -in "/etc/letsencrypt/live/$domain/fullchain.pem" \
    -noout -checkhost "$domain" >/dev/null || die "证书主机名校验失败：$domain"
done
if [[ "$action" == cutover ]]; then
  [[ -f "/etc/letsencrypt/live/$new_domain/fullchain.pem" ]] || die "缺少新主域证书：$new_domain"
  openssl x509 -in "/etc/letsencrypt/live/$new_domain/fullchain.pem" \
    -noout -checkhost "$new_domain" >/dev/null || die "新主域证书主机名校验失败"
fi

stamp="$(date -u +%Y%m%dT%H%M%SZ)-$$"
state_dir="$state_root/$stamp"
install -d -m 0700 "$state_dir"
[[ -f "$live_config" ]] && cp -a "$live_config" "$state_dir/grandumi-production.before"
[[ -f "$mode_file" ]] && cp -a "$mode_file" "$state_dir/primary-domain-mode.before"
[[ -L "$precut_site_enabled" || -f "$precut_site_enabled" ]] && touch "$state_dir/precut-was-enabled"

runtime_files=()
for slot in a b; do
  runtime_file="/opt/grandumi/slots/$slot/frontend/public/network-endpoints.json"
  if [[ -f "$runtime_file" ]]; then
    runtime_files+=("$runtime_file")
    cp -a "$runtime_file" "$state_dir/network-endpoints-$slot.before"
  fi
done

committed=0
rollback_failed_switch() {
  status="${1:-$?}"
  [[ "$committed" == 0 ]] || exit "$status"
  set +e
  if [[ -f "$state_dir/grandumi-production.before" ]]; then
    cp -a "$state_dir/grandumi-production.before" "$live_config"
  fi
  if [[ -f "$state_dir/primary-domain-mode.before" ]]; then
    cp -a "$state_dir/primary-domain-mode.before" "$mode_file"
  else
    rm -f "$mode_file"
  fi
  for slot in a b; do
    backup="$state_dir/network-endpoints-$slot.before"
    target="/opt/grandumi/slots/$slot/frontend/public/network-endpoints.json"
    [[ -f "$backup" ]] && cp -a "$backup" "$target"
  done
  if [[ -f "$state_dir/precut-was-enabled" ]]; then
    ln -sfn "$precut_site_available" "$precut_site_enabled"
  else
    rm -f "$precut_site_enabled"
  fi
  nginx -t && systemctl reload nginx
  echo "域名切换失败，已恢复执行前配置；恢复证据：$state_dir" >&2
  exit "$status"
}
trap rollback_failed_switch ERR
trap 'rollback_failed_switch 130' INT TERM

if [[ "$action" == cutover ]]; then
  selected_config="$ygo_config"
  selected_mode=ygo
  runtime_json='{"version":1,"hosts":["ygo.grand-umi.com","direct.grand-umi.com"],"endpoints":[{"url":"wss://direct.grand-umi.com/ws","enabled":true},{"url":"wss://ygo.grand-umi.com/ws","enabled":true}]}'
  rm -f "$precut_site_enabled"
else
  selected_config="$legacy_config"
  selected_mode=legacy
  runtime_json='{"version":1,"hosts":["grand-umi.com","direct.grand-umi.com"],"endpoints":[{"url":"wss://direct.grand-umi.com/ws","enabled":true},{"url":"wss://grand-umi.com/ws","enabled":true}]}'
  if [[ -f "/etc/letsencrypt/live/$new_domain/fullchain.pem" ]]; then
    install -m 0644 "$precut_config" "$precut_site_available"
    ln -sfn "$precut_site_available" "$precut_site_enabled"
  fi
fi

# 先更新所有现存 A/B 槽的运行时线路，再持久化模式，最后替换 Nginx。
# 即使进程被 SIGKILL，下一次 bootstrap 也会按模式文件收敛到同一目标；
# 在模式写入前中断时，旧站点会忽略不包含旧 Host 的 ygo 运行时清单。
for runtime_file in "${runtime_files[@]}"; do
  printf '%s\n' "$runtime_json" > "$runtime_file.next"
  chown --reference="$runtime_file" "$runtime_file.next"
  chmod --reference="$runtime_file" "$runtime_file.next"
  mv "$runtime_file.next" "$runtime_file"
done
printf '%s\n' "$selected_mode" > "$mode_file.next"
mv "$mode_file.next" "$mode_file"

install -m 0644 "$selected_config" "$live_config.next"
mv "$live_config.next" "$live_config"
ln -sfn "$live_config" "$live_site"
nginx -t
systemctl reload nginx

probe_code() {
  local domain="$1"
  curl -sS --max-time 15 --resolve "$domain:443:127.0.0.1" \
    -o /dev/null -w '%{http_code}' "https://$domain/"
}
old_code="$(probe_code "$old_domain")"
direct_code="$(probe_code "$direct_domain")"
[[ "$direct_code" != 000 ]] || die "直连域 TLS 探测失败"
if [[ "$action" == cutover ]]; then
  new_code="$(probe_code "$new_domain")"
  [[ "$old_code" == 403 ]] || die "旧主域未拒绝访问：HTTP $old_code"
  [[ "$new_code" != 000 && "$new_code" != 403 && "$new_code" != 503 ]] \
    || die "新主域未进入正式站点：HTTP $new_code"
else
  [[ "$old_code" != 000 && "$old_code" != 403 ]] || die "旧主域回退验证失败：HTTP $old_code"
  if [[ -f "/etc/letsencrypt/live/$new_domain/fullchain.pem" ]]; then
    new_code="$(probe_code "$new_domain")"
    [[ "$new_code" == 503 ]] || die "新主域回退后未恢复隔离：HTTP $new_code"
  else
    new_code=无证书
  fi
fi

printf '%s\n' "$selected_mode" > "$state_dir/completed-mode"
committed=1
trap - ERR INT TERM

echo "正式主域模式已切换为 $selected_mode；旧域 HTTP=$old_code，新域 HTTP=$new_code，直连 HTTP=$direct_code。"
echo "本次可恢复快照：$state_dir"
