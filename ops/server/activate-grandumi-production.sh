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
[[ -f "$import_dir/.ready" ]] || die "最终正式数据尚未标记就绪"
for name in players.db ranked.db leader-stats.db; do
  [[ -s "$import_dir/$name" ]] || die "缺少最终正式数据：$name"
  [[ "$(sqlite3 "$import_dir/$name" 'PRAGMA integrity_check;')" == ok ]] || die "最终正式数据完整性失败：$name"
done

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
    systemctl stop grandumi-production-frontend.service grandumi-production-backend.service || true
    for name in players.db ranked.db leader-stats.db; do
      [[ -f "$archive_dir/$name" ]] && install -o grandumi -g grandumi -m 0640 "$archive_dir/$name" "/data/grandumi/$name"
    done
    systemctl start grandumi-candidate-backend.service grandumi-candidate-frontend.service || true
  fi
  exit "$status"
}
trap rollback ERR

install -d -m 0750 "$archive_dir"
systemctl stop grandumi-candidate-frontend.service grandumi-candidate-backend.service
switched=1
for name in players.db ranked.db leader-stats.db; do
  [[ -f "/data/grandumi/$name" ]] && cp -a "/data/grandumi/$name" "$archive_dir/$name"
  install -o grandumi -g grandumi -m 0640 "$import_dir/$name" "/data/grandumi/$name"
done

systemctl enable grandumi-production-backend.service grandumi-production-frontend.service
systemctl disable grandumi-candidate-backend.service grandumi-candidate-frontend.service || true
systemctl start grandumi-production-backend.service
curl -fsS --retry 20 --retry-delay 1 --retry-connrefused http://127.0.0.1:8080/ready >/dev/null
curl -fsS http://127.0.0.1:8080/version | grep -Fq "$target" || die "正式后端版本与目标提交不一致"
systemctl start grandumi-production-frontend.service
curl -fsS --retry 20 --retry-delay 1 --retry-connrefused http://127.0.0.1:3000/ >/dev/null
nginx -t
systemctl reload nginx
curl -kfsS --resolve grand-umi.com:443:127.0.0.1 https://grand-umi.com/backend/ready >/dev/null

trap - ERR
echo "新正式服服务已激活：$target；测试数据归档：$archive_dir"
