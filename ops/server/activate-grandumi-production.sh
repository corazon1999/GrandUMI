#!/usr/bin/env bash
set -Eeuo pipefail

target="${1:-}"
repo=/opt/grandumi
import_dir=/data/grandumi-import/final
archive_dir="/data/grandumi-archives/pre-production-$(date -u +%Y%m%dT%H%M%SZ)"
switched=0

die() { echo "错误：$*" >&2; exit 1; }
[[ "$target" =~ ^[0-9a-f]{40}$ ]] || die "必须提供 40 位提交号"
[[ "$(tr -d '\r\n' < /var/lib/grandumi-production-staged)" == "$target" ]] || die "预构建版本与目标版本不一致"
[[ -d "$repo/releases/$target/backend" && -d "$repo/releases/$target/frontend" ]] \
  || die "目标 A/B 发布包不存在"

# 已进入 A/B 模式后，发布只切换到空闲槽位；失败由切换脚本自动回滚。
active_slot="$(cat /var/lib/grandumi-ha/active-slot 2>/dev/null || true)"
if [[ "$active_slot" =~ ^[ab]$ \
      && -e "$repo/slots/$active_slot/backend/GrandUMIServer.dll" ]] \
      && systemctl is-active --quiet "grandumi-production-backend@$active_slot.service" \
      && systemctl is-active --quiet "grandumi-production-frontend@$active_slot.service"; then
  /usr/local/sbin/grandumi-production-switch --release "$target"
  active="$(cat /var/lib/grandumi-ha/active-slot)"
  port=8080; [[ "$active" == b ]] && port=8082
  curl -fsS "http://127.0.0.1:$port/version" | grep -Fq "$target" \
    || die "切换后版本与目标提交不一致"
  curl -kfsS --resolve grand-umi.com:443:127.0.0.1 \
    https://grand-umi.com/backend/ready >/dev/null
  curl -fsS --resolve direct.grand-umi.com:443:127.0.0.1 \
    https://direct.grand-umi.com/backend/ready >/dev/null
  printf '%s\n' "$target" > /var/lib/grandumi-production-deployed.next
  mv /var/lib/grandumi-production-deployed.next /var/lib/grandumi-production-deployed
  echo "新正式服 A/B 发布成功：$target（活动槽位 $active）"
  exit 0
fi

# 以下仅用于从候选/旧单槽服务首次迁入 A/B 架构。已经运行中的正式数据
# 永远优先原地接管，不能被历史导入目录覆盖。
database_names=(players.db ranked.db leader-stats.db)
existing_count=0
for name in "${database_names[@]}"; do
  [[ -s "/data/grandumi/$name" ]] && existing_count=$((existing_count + 1))
done

if [[ "$existing_count" == "${#database_names[@]}" ]]; then
  data_source=existing
  for name in "${database_names[@]}"; do
    [[ "$(sqlite3 "/data/grandumi/$name" 'PRAGMA integrity_check;')" == ok ]] \
      || die "现有正式数据完整性失败：$name"
  done
elif [[ "$existing_count" == 0 ]]; then
  data_source=import
  [[ -f "$import_dir/.ready" ]] || die "空白服务器首次激活所需数据尚未标记就绪"
  for name in "${database_names[@]}"; do
    [[ -s "$import_dir/$name" ]] || die "缺少首次导入数据：$name"
    [[ "$(sqlite3 "$import_dir/$name" 'PRAGMA integrity_check;')" == ok ]] \
      || die "首次导入数据完整性失败：$name"
  done
else
  die "正式数据目录只存在 $existing_count/${#database_names[@]} 个数据库，拒绝覆盖或激活"
fi

# 候选服自动部署可能在预构建后重新创建旧站点；正式双域名站点已经同时
# 服务 grand-umi.com 与 candidate.grand-umi.com，激活前必须清理重复监听。
systemctl daemon-reload
ln -sfn /etc/nginx/sites-available/grandumi-production /etc/nginx/sites-enabled/grandumi-production
rm -f /etc/nginx/sites-enabled/grandumi-candidate
nginx -t
systemctl reload nginx

rollback() {
  status=$?
  if [[ "$switched" == 1 ]]; then
    systemctl stop grandumi-production-frontend@a.service grandumi-production-backend@a.service || true
    if [[ "$data_source" == existing ]]; then
      systemctl enable grandumi-production-backend.service grandumi-production-frontend.service || true
      systemctl start grandumi-production-backend.service grandumi-production-frontend.service || true
    else
      for name in "${database_names[@]}"; do
        [[ -f "$archive_dir/$name" ]] && install -o grandumi -g grandumi -m 0640 "$archive_dir/$name" "/data/grandumi/$name"
      done
      systemctl enable grandumi-candidate-backend.service grandumi-candidate-frontend.service || true
      systemctl start grandumi-candidate-backend.service grandumi-candidate-frontend.service || true
    fi
  fi
  exit "$status"
}
trap rollback ERR

install -d -m 0750 "$archive_dir"
systemctl stop grandumi-candidate-frontend.service grandumi-candidate-backend.service \
  grandumi-production-frontend.service grandumi-production-backend.service || true
switched=1
for name in "${database_names[@]}"; do
  [[ -f "/data/grandumi/$name" ]] && cp -a "/data/grandumi/$name" "$archive_dir/$name"
  if [[ "$data_source" == import ]]; then
    install -o grandumi -g grandumi -m 0640 "$import_dir/$name" "/data/grandumi/$name"
  fi
done

ln -sfn "$repo/releases/$target/backend" "$repo/slots/a/backend"
ln -sfn "$repo/releases/$target/frontend" "$repo/slots/a/frontend"
ln -sfn "$repo/releases/$target/backend" "$repo/slots/b/backend"
ln -sfn "$repo/releases/$target/frontend" "$repo/slots/b/frontend"
printf 'a\n' > /var/lib/grandumi-ha/active-slot
printf 'b\n' > /var/lib/grandumi-ha/standby-slot
printf 'proxy_pass http://127.0.0.1:8080;\n' > /etc/nginx/snippets/grandumi-active-backend.conf
printf 'proxy_pass http://127.0.0.1:3000;\n' > /etc/nginx/snippets/grandumi-active-frontend.conf
printf 'root /opt/grandumi/slots/a/frontend/public;\n' > /etc/nginx/snippets/grandumi-active-assets.conf
printf 'root /opt/grandumi/slots/a/frontend;\n' > /etc/nginx/snippets/grandumi-active-frontend-files.conf
systemctl enable grandumi-production-backend@a.service grandumi-production-frontend@a.service
systemctl disable grandumi-production-backend.service grandumi-production-frontend.service || true
systemctl disable grandumi-candidate-backend.service grandumi-candidate-frontend.service || true
systemctl start grandumi-production-backend@a.service
curl -fsS --retry 20 --retry-delay 1 --retry-connrefused http://127.0.0.1:8080/ready >/dev/null
curl -fsS http://127.0.0.1:8080/version | grep -Fq "$target" || die "正式后端版本与目标提交不一致"
systemctl start grandumi-production-frontend@a.service
curl -fsS --retry 20 --retry-delay 1 --retry-connrefused http://127.0.0.1:3000/ >/dev/null
nginx -t
systemctl reload nginx
systemctl enable --now grandumi-production-health.timer
curl -kfsS --resolve grand-umi.com:443:127.0.0.1 https://grand-umi.com/backend/ready >/dev/null
curl -fsS --resolve direct.grand-umi.com:443:127.0.0.1 \
  https://direct.grand-umi.com/backend/ready >/dev/null
printf '%s\n' "$target" > /var/lib/grandumi-production-deployed.next
mv /var/lib/grandumi-production-deployed.next /var/lib/grandumi-production-deployed

trap - ERR
echo "新正式服服务已激活：$target；数据来源：$data_source；切换前归档：$archive_dir"
