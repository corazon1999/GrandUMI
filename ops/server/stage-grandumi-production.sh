#!/usr/bin/env bash
set -Eeuo pipefail

repo=/opt/grandumi
stage_script="$(readlink -f "${BASH_SOURCE[0]}")"
target="${1:-}"
production_ip="${GRANDUMI_PRODUCTION_IP:-103.146.230.37}"
shared_asset_root=/www
production_asset_root="$repo/opcgpro-web/public"
card_asset_dirs=(cards-thumb cards-webp)

die() { echo "错误：$*" >&2; exit 1; }
[[ "$production_ip" == "103.146.230.37" ]] || die "拒绝部署到未登记主机：$production_ip"
[[ "$target" =~ ^[0-9a-f]{40}$ ]] || die "必须提供 40 位提交号"
git -C "$repo" cat-file -e "$target^{commit}" 2>/dev/null || die "新正式服仓库中不存在提交 $target"
command -v rsync >/dev/null || die "缺少 rsync，无法创建节省磁盘的版本化静态资源"

# 卡图不进入 Git，发布 worktree 中不会包含这些目录。先把正式服持久资源同步到
# /www，再在每个 A/B 前端槽内创建符号链接，避免切槽后整批卡图返回 404。
for asset_dir in "${card_asset_dirs[@]}"; do
  source_dir="$production_asset_root/$asset_dir"
  shared_dir="$shared_asset_root/$asset_dir"
  [[ -d "$source_dir" ]] || die "正式服卡图资源目录不存在：$source_dir"
  install -d -m 0755 "$shared_dir"
  rsync -a "$source_dir/" "$shared_dir/"
  [[ -n "$(find "$shared_dir" -type f -print -quit)" ]] || die "正式服共享卡图目录为空：$shared_dir"
done

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

# 清单会随 Git 发布，但卡图二进制位于共享目录。必须在构建前逐项核对，避免清单先上线、
# 异画文件仍未同步时把整批 404 带入正式版本。
node "$build_root/opcgpro-web/scripts/check-card-image-manifest.mjs" \
  "$build_root/opcgpro-web/public/data/imageManifest.json" \
  "$shared_asset_root"

rm -rf "$publish_next" "$release_dir/frontend.next"
dotnet publish "$build_root/服务端WebSocket/GrandUMIServer.csproj" -c Release -o "$publish_next" --nologo \
  -p:InformationalVersion="1.0.0+$target" \
  -p:IncludeSourceRevisionInInformationalVersion=false

cd "$build_root/opcgpro-web"
npm ci --no-audit --no-fund
cat > public/network-endpoints.json <<'JSON'
{"version":1,"hosts":["ygo.grand-umi.com","direct.grand-umi.com"],"endpoints":[{"url":"wss://direct.grand-umi.com/ws","enabled":true},{"url":"wss://ygo.grand-umi.com/ws","enabled":true}]}
JSON
if ! NEXT_PUBLIC_WS_URL='wss://ygo.grand-umi.com/ws' \
    NEXT_PUBLIC_ASSET_ORIGIN='https://assets.grand-umi.com' \
    CARD_BACK_API_URL=http://127.0.0.1:8080 npm run build; then
  die "新正式服前端构建失败"
fi

frontend_next="$release_dir/frontend.next"
mkdir -p "$frontend_next"
cp -a .next package.json package-lock.json "$frontend_next/"
active_slot="$(cat /var/lib/grandumi-ha/active-slot 2>/dev/null || echo a)"
previous_frontend="$repo/slots/$active_slot/frontend"
# Cloudflare 可能在短时间内继续返回上一版本 HTML；保留旧哈希分块，避免缓存入口
# 在新版本切换后引用已删除文件而导致白屏。只补缺文件，绝不覆盖本次构建产物。
if [[ -d "$previous_frontend/.next/static" ]]; then
  rsync -a --ignore-existing "$previous_frontend/.next/static/" "$frontend_next/.next/static/"
fi
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
for asset_dir in "${card_asset_dirs[@]}"; do
  slot_asset_path="$frontend_next/public/$asset_dir"
  [[ ! -e "$slot_asset_path" && ! -L "$slot_asset_path" ]] \
    || die "前端发布槽卡图路径已存在，拒绝覆盖：$slot_asset_path"
  ln -s "$shared_asset_root/$asset_dir" "$slot_asset_path"
done
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
install -m 0755 "$build_root/ops/server/grandumi-matchlog-maintenance.sh" /usr/local/sbin/grandumi-matchlog-maintenance
install -m 0755 "$build_root/ops/server/verify-grandumi-ha.sh" /usr/local/sbin/verify-grandumi-ha
install -d -m 0755 /var/lib/grandumi-admin-deploy/status
install -d -o grandumi -g grandumi -m 0750 /var/lib/grandumi-admin-deploy/requests
install -m 0755 "$build_root/ops/server/grandumi-admin-deploy.sh" /usr/local/sbin/grandumi-admin-deploy
install -m 0644 "$build_root/ops/server/grandumi-admin-deploy.service" /etc/systemd/system/grandumi-admin-deploy.service
install -m 0644 "$build_root/ops/server/grandumi-admin-deploy.path" /etc/systemd/system/grandumi-admin-deploy.path
install -m 0644 "$build_root/ops/server/grandumi-production-health.service" /etc/systemd/system/grandumi-production-health.service
install -m 0644 "$build_root/ops/server/grandumi-production-health.timer" /etc/systemd/system/grandumi-production-health.timer
install -m 0644 "$build_root/ops/server/grandumi-matchlog-maintenance.service" /etc/systemd/system/grandumi-matchlog-maintenance.service
install -m 0644 "$build_root/ops/server/grandumi-matchlog-maintenance.timer" /etc/systemd/system/grandumi-matchlog-maintenance.timer
systemctl daemon-reload
systemctl enable --now grandumi-matchlog-maintenance.timer grandumi-admin-deploy.path

printf '%s\n' "$target" > /var/lib/grandumi-production-staged
echo "新正式服 A/B 发布包已在受限资源组内预构建，尚未切换服务：$target"
