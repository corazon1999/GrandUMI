#!/usr/bin/env bash
set -Eeuo pipefail

repo=/opt/grandumi
stage_script="$(readlink -f "${BASH_SOURCE[0]}")"
target="${1:-}"
production_ip="${GRANDUMI_PRODUCTION_IP:-103.146.230.37}"

die() { echo "错误：$*" >&2; exit 1; }
[[ "$production_ip" == "103.146.230.37" ]] || die "拒绝部署到未登记主机：$production_ip"
[[ "$target" =~ ^[0-9a-f]{40}$ ]] || die "必须提供 40 位提交号"
git -C "$repo" cat-file -e "$target^{commit}" 2>/dev/null || die "新正式服仓库中不存在提交 $target"
command -v rsync >/dev/null || die "缺少 rsync，无法创建节省磁盘的版本化静态资源"

# 构建任务自动进入低优先级 slice，避免 npm/dotnet 抢占在线对局 CPU、内存和磁盘。
if [[ "${GRANDUMI_BUILD_SCOPED:-0}" != 1 ]]; then
  exec systemd-run --quiet --wait --pipe --collect \
    --unit="grandumi-build-${target:0:12}" \
    --slice=grandumi-build.slice \
    --setenv=GRANDUMI_BUILD_SCOPED=1 \
    --setenv=GRANDUMI_PRODUCTION_IP="$production_ip" \
    /usr/bin/bash "$stage_script" "$target"
fi

release_dir="$repo/releases/$target"
build_root="/opt/grandumi-build/$target"
publish_next="$release_dir/backend.next"
cleanup() {
  git -C "$repo" worktree remove --force "$build_root" >/dev/null 2>&1 || true
  rm -rf "$build_root"
}
trap cleanup EXIT
cleanup
mkdir -p "$(dirname "$build_root")" "$release_dir"
git -C "$repo" worktree add --detach "$build_root" "$target" >/dev/null

rm -rf "$publish_next" "$release_dir/frontend.next"
dotnet publish "$build_root/服务端WebSocket/GrandUMIServer.csproj" -c Release -o "$publish_next" --nologo \
  -p:InformationalVersion="1.0.0+$target" \
  -p:IncludeSourceRevisionInInformationalVersion=false

cd "$build_root/opcgpro-web"
npm ci --no-audit --no-fund
cat > public/network-endpoints.json <<'JSON'
{"version":1,"hosts":["grand-umi.com"],"endpoints":[{"url":"wss://grand-umi.com/ws","enabled":true}]}
JSON
if ! NEXT_PUBLIC_WS_URL='wss://grand-umi.com/ws' \
    NEXT_PUBLIC_ASSET_ORIGIN='https://grand-umi.com' \
    CARD_BACK_API_URL=http://127.0.0.1:8080 npm run build; then
  die "新正式服前端构建失败"
fi

frontend_next="$release_dir/frontend.next"
mkdir -p "$frontend_next"
cp -a .next package.json package-lock.json "$frontend_next/"
active_slot="$(cat /var/lib/grandumi-ha/active-slot 2>/dev/null || echo a)"
previous_frontend="$repo/slots/$active_slot/frontend"
if [[ -d "$previous_frontend/node_modules" ]]; then
  rsync -a --delete --link-dest="$previous_frontend/node_modules" node_modules/ "$frontend_next/node_modules/"
else
  rsync -a --delete node_modules/ "$frontend_next/node_modules/"
fi
mkdir -p "$frontend_next/public"
previous_public="$previous_frontend/public"
if [[ -d "$previous_public" ]]; then
  # 约 2 GB 卡图绝大多数版本不变；未变化文件与活动版本硬链接，回滚仍保留独立目录。
  rsync -a --delete --link-dest="$previous_public" public/ "$frontend_next/public/"
else
  rsync -a --delete public/ "$frontend_next/public/"
fi
rm -rf "$release_dir/backend" "$release_dir/frontend"
mv "$publish_next" "$release_dir/backend"
mv "$frontend_next" "$release_dir/frontend"
chown -R grandumi:grandumi "$release_dir"

install -m 0644 "$build_root/ops/server/grandumi-production-backend.service" /etc/systemd/system/grandumi-production-backend.service
install -m 0644 "$build_root/ops/server/grandumi-production-frontend.service" /etc/systemd/system/grandumi-production-frontend.service
install -m 0644 "$build_root/ops/server/grandumi-production.slice" /etc/systemd/system/grandumi-production.slice
install -m 0644 "$build_root/ops/server/grandumi-build.slice" /etc/systemd/system/grandumi-build.slice
install -m 0644 "$build_root/ops/server/grandumi-production-backend@.service" /etc/systemd/system/grandumi-production-backend@.service
install -m 0644 "$build_root/ops/server/grandumi-production-frontend@.service" /etc/systemd/system/grandumi-production-frontend@.service
install -m 0755 "$build_root/ops/server/grandumi-production-switch.sh" /usr/local/sbin/grandumi-production-switch
install -m 0755 "$build_root/ops/server/grandumi-production-health-check.sh" /usr/local/sbin/grandumi-production-health-check
install -m 0755 "$build_root/ops/server/verify-grandumi-ha.sh" /usr/local/sbin/verify-grandumi-ha
install -m 0644 "$build_root/ops/server/grandumi-production-health.service" /etc/systemd/system/grandumi-production-health.service
install -m 0644 "$build_root/ops/server/grandumi-production-health.timer" /etc/systemd/system/grandumi-production-health.timer
systemctl daemon-reload

printf '%s\n' "$target" > /var/lib/grandumi-production-staged
echo "新正式服 A/B 发布包已在受限资源组内预构建，尚未切换服务：$target"
