#!/usr/bin/env bash
set -Eeuo pipefail

action="${1:-}"
source_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

mutations_started=0
committed=0
restoring=0
state_dir=""

die() {
  local message="$*"
  echo "错误：$message" >&2
  if (( mutations_started == 1 )); then
    rollback_failed_switch 1 "$message"
  fi
  exit 1
}

test_mode="${GRANDUMI_DOMAIN_CUTOVER_TEST_MODE:-0}"
test_root="${GRANDUMI_DOMAIN_CUTOVER_TEST_ROOT:-}"
if [[ "$test_mode" == 1 ]]; then
  [[ -n "$test_root" && "$test_root" == /* && "$test_root" != / ]] \
    || die "测试根目录必须是非根绝对路径"
  [[ -f "$test_root/.grandumi-domain-cutover-test-root" ]] \
    || die "测试根目录缺少专用标记文件"
elif [[ "$test_mode" != 0 || -n "$test_root" ]]; then
  die "测试根目录只能与 GRANDUMI_DOMAIN_CUTOVER_TEST_MODE=1 一起使用"
fi

root_path() {
  local path="$1"
  if [[ "$test_mode" == 1 ]]; then
    printf '%s%s' "$test_root" "$path"
  else
    printf '%s' "$path"
  fi
}

system_legacy_config="$(root_path /etc/nginx/sites-available/grandumi-production-legacy)"
system_ygo_config="$(root_path /etc/nginx/sites-available/grandumi-production-ygo)"
system_precut_config="$(root_path /etc/nginx/sites-available/grandumi-ygo-precut-template)"
if [[ -f "$source_root/ops/server/grandumi-production.nginx" ]]; then
  legacy_config="$source_root/ops/server/grandumi-production.nginx"
  ygo_config="$source_root/ops/server/grandumi-production-ygo.nginx"
  precut_config="$source_root/ops/server/grandumi-ygo-precut.nginx"
else
  legacy_config="$system_legacy_config"
  ygo_config="$system_ygo_config"
  precut_config="$system_precut_config"
fi

live_config="$(root_path /etc/nginx/sites-available/grandumi-production)"
live_site="$(root_path /etc/nginx/sites-enabled/grandumi-production)"
precut_site_available="$(root_path /etc/nginx/sites-available/grandumi-ygo-precut)"
precut_site_enabled="$(root_path /etc/nginx/sites-enabled/grandumi-ygo-precut)"
mode_file="$(root_path /etc/grandumi/primary-domain-mode)"
state_root="$(root_path /var/lib/grandumi-domain-cutover)"
lock_dir="$(root_path /run/lock)"
lock_file="$lock_dir/grandumi-domain-cutover.lock"
old_domain=grand-umi.com
new_domain=ygo.grand-umi.com
direct_domain=direct.grand-umi.com
assets_domain=assets.grand-umi.com
assets_probe_path=/sprites-thumb/CardBack.webp

probe_deadline_seconds="${GRANDUMI_DOMAIN_CUTOVER_PROBE_TIMEOUT_SECONDS:-30}"
probe_interval_seconds="${GRANDUMI_DOMAIN_CUTOVER_PROBE_INTERVAL_SECONDS:-1}"
[[ "$probe_deadline_seconds" =~ ^[1-9][0-9]*$ && "$probe_deadline_seconds" -le 300 ]] \
  || die "探测总截止时间必须是 1 到 300 秒的整数"
[[ "$probe_interval_seconds" =~ ^[1-9][0-9]*$ && "$probe_interval_seconds" -le 10 ]] \
  || die "探测间隔必须是 1 到 10 秒的整数"

cert_path() {
  root_path "/etc/letsencrypt/live/$1/fullchain.pem"
}

probe_code() {
  local domain="$1"
  local scheme="$2"
  local path="$3"
  local deadline="$4"
  local port=443
  local remaining max_time code

  if [[ "$scheme" == http ]]; then
    port=80
  fi
  remaining=$(( deadline - SECONDS ))
  if (( remaining <= 0 )); then
    printf '000'
    return 0
  fi
  max_time="$remaining"
  if (( max_time > 5 )); then
    max_time=5
  fi
  code="$(curl --noproxy '*' -sS --connect-timeout "$max_time" --max-time "$max_time" \
    --resolve "$domain:$port:127.0.0.1" -o /dev/null -w '%{http_code}' \
    "$scheme://$domain$path" 2>/dev/null || true)"
  if [[ ! "$code" =~ ^[0-9]{3}$ ]]; then
    code=000
  fi
  printf '%s' "$code"
}

is_active_site_code() {
  local code="$1"
  [[ "$code" != 000 && "$code" != 403 && "$code" != 503 ]]
}

collect_probe_codes() {
  local deadline="$1"
  last_old_http_code="$(probe_code "$old_domain" http / "$deadline")"
  last_old_https_code="$(probe_code "$old_domain" https / "$deadline")"
  last_new_http_code="$(probe_code "$new_domain" http / "$deadline")"
  last_new_https_code="$(probe_code "$new_domain" https / "$deadline")"
  last_direct_https_code="$(probe_code "$direct_domain" https / "$deadline")"
  last_assets_https_code="$(probe_code "$assets_domain" https "$assets_probe_path" "$deadline")"
}

mode_probe_is_stable() {
  local expected_mode="$1"
  if [[ "$expected_mode" == ygo ]]; then
    [[ "$last_old_http_code" == 403 && "$last_old_https_code" == 403 ]] || return 1
    [[ "$last_new_http_code" == 308 ]] || return 1
    is_active_site_code "$last_new_https_code" || return 1
  else
    [[ "$last_old_http_code" == 308 ]] || return 1
    is_active_site_code "$last_old_https_code" || return 1
    if (( has_new_domain_certificate == 1 )); then
      [[ "$last_new_http_code" == 503 && "$last_new_https_code" == 503 ]] || return 1
    fi
  fi
  is_active_site_code "$last_direct_https_code" || return 1
  [[ "$last_assets_https_code" == 200 ]] || return 1
}

wait_for_mode() {
  local expected_mode="$1"
  local phase="$2"
  local deadline=$(( SECONDS + probe_deadline_seconds ))
  local attempt=0
  local remaining sleep_seconds

  while :; do
    attempt=$(( attempt + 1 ))
    collect_probe_codes "$deadline"
    echo "[域名切换][$phase] 第 $attempt 次探测：旧域 HTTP=$last_old_http_code HTTPS=$last_old_https_code；新域 HTTP=$last_new_http_code HTTPS=$last_new_https_code；直连 HTTPS=$last_direct_https_code；资源 HTTPS=$last_assets_https_code" >&2
    if mode_probe_is_stable "$expected_mode"; then
      echo "[域名切换][$phase] 已收敛到 $expected_mode 模式，共探测 $attempt 次。" >&2
      return 0
    fi
    remaining=$(( deadline - SECONDS ))
    if (( remaining <= 0 )); then
      echo "[域名切换][$phase] 在 ${probe_deadline_seconds} 秒总截止时间内未收敛到 $expected_mode 模式。" >&2
      return 1
    fi
    sleep_seconds="$probe_interval_seconds"
    if (( sleep_seconds > remaining )); then
      sleep_seconds="$remaining"
    fi
    echo "[域名切换][$phase] 尚未收敛，${sleep_seconds} 秒后重试（剩余不超过 ${remaining} 秒）。" >&2
    sleep "$sleep_seconds"
  done
}

snapshot_path_entry() {
  local target="$1"
  local name="$2"
  if [[ -L "$target" ]]; then
    readlink "$target" > "$state_dir/$name.symlink-target"
  elif [[ -f "$target" ]]; then
    cp -a "$target" "$state_dir/$name.file-before"
  elif [[ -e "$target" ]]; then
    die "不支持快照非文件路径：$target"
  fi
}

restore_path_entry() {
  local target="$1"
  local name="$2"
  local link_target
  rm -f "$target" || return 1
  if [[ -f "$state_dir/$name.symlink-target" ]]; then
    link_target="$(<"$state_dir/$name.symlink-target")"
    ln -sfn "$link_target" "$target" || return 1
  elif [[ -f "$state_dir/$name.file-before" ]]; then
    cp -a "$state_dir/$name.file-before" "$target" || return 1
  fi
}

rollback_failed_switch() {
  local status="${1:-1}"
  local reason="${2:-未捕获的执行错误}"
  local restore_ok=1

  if (( committed == 1 )); then
    trap - ERR INT TERM
    exit "$status"
  fi
  if (( mutations_started == 0 )); then
    trap - ERR INT TERM
    exit "$status"
  fi
  if (( restoring == 1 )); then
    trap - ERR INT TERM
    echo "错误：恢复流程再次失败，已阻止递归恢复；证据目录：$state_dir" >&2
    exit 70
  fi

  restoring=1
  trap - ERR INT TERM
  set +e
  printf 'status=%s\nreason=%s\n' "$status" "$reason" > "$state_dir/restore-started" || restore_ok=0

  if [[ -f "$state_dir/grandumi-production.before" ]]; then
    cp -a "$state_dir/grandumi-production.before" "$live_config" || restore_ok=0
  else
    restore_ok=0
  fi
  if [[ -f "$state_dir/primary-domain-mode.before" ]]; then
    cp -a "$state_dir/primary-domain-mode.before" "$mode_file" || restore_ok=0
  else
    rm -f "$mode_file" || restore_ok=0
  fi
  for slot in a b; do
    local backup="$state_dir/network-endpoints-$slot.before"
    local target
    target="$(root_path "/opt/grandumi/slots/$slot/frontend/public/network-endpoints.json")"
    if [[ -f "$backup" ]]; then
      cp -a "$backup" "$target" || restore_ok=0
    fi
    rm -f "$target.next" || restore_ok=0
  done
  restore_path_entry "$precut_site_available" precut-available || restore_ok=0
  restore_path_entry "$precut_site_enabled" precut-enabled || restore_ok=0
  restore_path_entry "$live_site" live-site || restore_ok=0
  rm -f "$mode_file.next" "$live_config.next" || restore_ok=0

  if (( restore_ok == 1 )); then
    nginx -t || restore_ok=0
  fi
  if (( restore_ok == 1 )); then
    systemctl reload nginx || restore_ok=0
  fi
  if (( restore_ok == 1 )); then
    wait_for_mode "$previous_mode" 恢复 || restore_ok=0
  fi

  if (( restore_ok == 1 )); then
    printf 'mode=%s\nstatus=%s\nreason=%s\n' "$previous_mode" "$status" "$reason" > "$state_dir/restore-complete" || restore_ok=0
  fi
  if (( restore_ok == 1 )); then
    echo "域名切换失败（$reason），已恢复执行前的 $previous_mode 配置并验证收敛；恢复证据：$state_dir" >&2
    exit "$status"
  fi

  printf 'mode=%s\nstatus=%s\nreason=%s\n' "$previous_mode" "$status" "$reason" > "$state_dir/restore-failed" 2>/dev/null
  echo "严重错误：域名切换失败（$reason），且恢复或恢复后验证失败；请保持正式后端停机并检查：$state_dir" >&2
  exit 70
}

[[ "$action" == cutover || "$action" == rollback ]] \
  || die "用法：$0 cutover|rollback"
if [[ "$test_mode" != 1 ]]; then
  [[ "$EUID" -eq 0 ]] || die "必须以 root 执行"
fi
[[ -f "$legacy_config" && -f "$ygo_config" && -f "$precut_config" ]] \
  || die "缺少域名切换 Nginx 配置"
[[ -f "$live_config" ]] || die "缺少当前正式站点配置：$live_config"
[[ ! -L "$live_config" ]] || die "当前正式站点配置不得是符号链接：$live_config"
command -v flock >/dev/null || die "缺少 flock，无法建立切换互斥锁"
command -v ss >/dev/null || die "缺少 ss，无法核验监听端口"
command -v curl >/dev/null || die "缺少 curl，无法执行本机源站探测"
command -v readlink >/dev/null || die "缺少 readlink，无法建立精确快照"

install -d -m 0755 "$lock_dir" "$(root_path /etc/grandumi)" "$state_root" \
  "$(root_path /etc/nginx/sites-available)" "$(root_path /etc/nginx/sites-enabled)"
exec 9>"$lock_file"
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

for domain in "$old_domain" "$direct_domain" "$assets_domain"; do
  certificate="$(cert_path "$domain")"
  [[ -f "$certificate" ]] || die "缺少证书：$domain"
  openssl x509 -in "$certificate" -noout -checkhost "$domain" >/dev/null \
    || die "证书主机名校验失败：$domain"
done
new_certificate="$(cert_path "$new_domain")"
has_new_domain_certificate=0
if [[ -f "$new_certificate" ]]; then
  has_new_domain_certificate=1
  openssl x509 -in "$new_certificate" -noout -checkhost "$new_domain" >/dev/null \
    || die "新主域证书主机名校验失败"
elif [[ "$action" == cutover ]]; then
  die "缺少新主域证书：$new_domain"
fi

stamp="$(date -u +%Y%m%dT%H%M%SZ)-$$"
state_dir="$state_root/$stamp"
[[ ! -e "$state_dir" && ! -L "$state_dir" ]] || die "状态快照目录已存在：$state_dir"
install -d -m 0700 "$state_dir"
cp -a "$live_config" "$state_dir/grandumi-production.before"
if [[ -f "$mode_file" ]]; then
  cp -a "$mode_file" "$state_dir/primary-domain-mode.before"
  previous_mode="$(tr -d '[:space:]' < "$state_dir/primary-domain-mode.before")"
else
  previous_mode=legacy
fi
[[ "$previous_mode" == legacy || "$previous_mode" == ygo ]] \
  || die "当前主域模式文件无效：$previous_mode"
snapshot_path_entry "$precut_site_available" precut-available
snapshot_path_entry "$precut_site_enabled" precut-enabled
snapshot_path_entry "$live_site" live-site

runtime_files=()
for slot in a b; do
  runtime_file="$(root_path "/opt/grandumi/slots/$slot/frontend/public/network-endpoints.json")"
  [[ ! -e "$runtime_file.next" && ! -L "$runtime_file.next" ]] \
    || die "发现残留运行时临时文件：$runtime_file.next"
  if [[ -f "$runtime_file" ]]; then
    runtime_files+=("$runtime_file")
    cp -a "$runtime_file" "$state_dir/network-endpoints-$slot.before"
  fi
done
[[ ! -e "$mode_file.next" && ! -L "$mode_file.next" ]] \
  || die "发现残留主域模式临时文件：$mode_file.next"
[[ ! -e "$live_config.next" && ! -L "$live_config.next" ]] \
  || die "发现残留 Nginx 临时配置：$live_config.next"

if [[ "$action" == cutover ]]; then
  selected_config="$ygo_config"
  selected_mode=ygo
  runtime_json='{"version":1,"hosts":["ygo.grand-umi.com","direct.grand-umi.com"],"endpoints":[{"url":"wss://direct.grand-umi.com/ws","enabled":true},{"url":"wss://ygo.grand-umi.com/ws","enabled":true}]}'
else
  selected_config="$legacy_config"
  selected_mode=legacy
  runtime_json='{"version":1,"hosts":["grand-umi.com","direct.grand-umi.com"],"endpoints":[{"url":"wss://direct.grand-umi.com/ws","enabled":true},{"url":"wss://grand-umi.com/ws","enabled":true}]}'
fi

trap 'rollback_failed_switch $? "未捕获的命令失败（行 $LINENO）"' ERR
trap 'rollback_failed_switch 130 "收到中断信号"' INT TERM
mutations_started=1

if [[ "$action" == cutover ]]; then
  rm -f "$precut_site_enabled"
else
  if (( has_new_domain_certificate == 1 )); then
    install -m 0644 "$precut_config" "$precut_site_available"
    ln -sfn "$precut_site_available" "$precut_site_enabled"
  fi
fi

# 先更新所有现存 A/B 槽的运行时线路，再持久化模式，最后替换 Nginx。
# 即使进程被 SIGKILL，下一次 bootstrap 也会按模式文件收敛到同一目标。
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

if ! wait_for_mode "$selected_mode" 切换; then
  die "Nginx reload 后未在总截止时间内收敛到 $selected_mode 模式"
fi

printf '%s\n' "$selected_mode" > "$state_dir/completed-mode"
committed=1
trap - ERR INT TERM

echo "正式主域模式已切换为 $selected_mode；旧域 HTTP=$last_old_http_code HTTPS=$last_old_https_code；新域 HTTP=$last_new_http_code HTTPS=$last_new_https_code；直连 HTTPS=$last_direct_https_code；资源 HTTPS=$last_assets_https_code。"
echo "本次可恢复快照：$state_dir"
