#!/usr/bin/env bash
set -Eeuo pipefail

repo=/opt/grandumi-test
state_dir=/var/lib/grandumi-release
target="$1"
force="$2"

die() {
  echo "错误：$*" >&2
  exit 1
}

backend_ready() {
  curl -fsS --retry 10 --retry-delay 1 --retry-connrefused \
    -o /dev/null http://127.0.0.1:8081/ready
}

backend_matches_target() {
  curl -fsS http://127.0.0.1:8081/version | grep -Fq "$target"
}

[[ "$target" =~ ^[0-9a-f]{40}$ ]] || die "必须提供完整的 40 位提交号。"
git -C "$repo" cat-file -e "$target^{commit}" 2>/dev/null || die "测试服仓库中不存在提交 $target。"

mkdir -p "$state_dir"
old="$(git -C "$repo" rev-parse HEAD)"

# prebuild 会更新这个生成文件；除此以外的受控文件改动都必须人工处理。
generated='opcgpro-web/src/data/dataVersion.ts'
dirty="$(git -C "$repo" -c core.quotepath=false diff --name-only |
  grep -Fvx "$generated" |
  grep -v '^服务端WebSocket/publish/' || true)"
[[ -z "$dirty" ]] || die "测试服存在未知受控文件改动：$dirty"
git -C "$repo" restore --worktree --staged -- "$generated" 2>/dev/null || true
git -C "$repo" restore --worktree --staged -- '服务端WebSocket/publish' 2>/dev/null || true

git -C "$repo" checkout --detach "$target"
changed="$(git -C "$repo" -c core.quotepath=false diff --name-only "$old" "$target" 2>/dev/null || true)"
need_back=0
need_front=0
need_npm=0
if [[ "$force" == "all" || "$old" == "$target" ]]; then
  need_back=1
  need_front=1
fi
grep -q '^服务端WebSocket/' <<<"$changed" && need_back=1 || true
grep -q '^opcgpro-web/' <<<"$changed" && need_front=1 || true
grep -Eq '^opcgpro-web/(package|package-lock)\.json$' <<<"$changed" && need_npm=1 || true

echo "测试服代码：$(git -C "$repo" rev-parse --short=12 "$old") -> $(git -C "$repo" rev-parse --short=12 "$target")"

if [[ "$need_back" == 1 ]]; then
  echo "构建测试服后端"
  next_publish="$repo/服务端WebSocket/publish.next"
  previous_publish="$repo/服务端WebSocket/publish.previous"
  rm -rf "$next_publish"
  dotnet publish "$repo/服务端WebSocket/GrandUMIServer.csproj" -c Release -o "$next_publish" --nologo \
    -p:InformationalVersion="1.0.0+$target" \
    -p:IncludeSourceRevisionInInformationalVersion=false

  echo "增量回填正式服 Leader 排行榜数据"
  production_stats_db="/opt/grandumi/服务端WebSocket/Data/leader-stats.db"
  mkdir -p "$(dirname "$production_stats_db")"
  dotnet "$next_publish/GrandUMIServer.dll" \
    --backfill-leader-stats \
    "/opt/grandumi/服务端WebSocket/MatchLogs" \
    "$production_stats_db"

  install -m 0644 "$repo/ops/server/grandumi-test-backend.service" \
    /etc/systemd/system/grandumi-test-backend.service
  systemctl daemon-reload

  rm -rf "$previous_publish"
  [[ -d "$repo/服务端WebSocket/publish" ]] && mv "$repo/服务端WebSocket/publish" "$previous_publish"
  mv "$next_publish" "$repo/服务端WebSocket/publish"
  if ! systemctl restart grandumi-test-backend.service \
      || ! systemctl is-active --quiet grandumi-test-backend.service \
      || ! backend_ready \
      || ! backend_matches_target; then
    rm -rf "$repo/服务端WebSocket/publish"
    [[ -d "$previous_publish" ]] && mv "$previous_publish" "$repo/服务端WebSocket/publish"
    systemctl restart grandumi-test-backend.service || true
    backend_ready || true
    die "测试服后端启动或就绪检查失败，已尝试回滚。"
  fi
fi

if [[ "$need_front" == 1 ]]; then
  echo "构建测试服前端"
  cd "$repo/opcgpro-web"

  # 卡图派生资源不进入 Git。每次前端部署先从正式服的只读资源库增量补齐测试服，
  # 再核对 manifest 中每张多画卡的最新异画，避免清单已更新但图鉴只能回退到正画。
  production_assets=/opt/grandumi/opcgpro-web/public
  test_assets=/opt/grandumi-test-assets
  for asset_dir in cards-thumb cards-webp; do
    source_dir="$production_assets/$asset_dir"
    target_dir="$test_assets/$asset_dir"
    [[ -d "$source_dir" ]] || die "正式服卡图资源目录不存在：$source_dir"
    mkdir -p "$target_dir"
    rsync -a "$source_dir/" "$target_dir/"
  done
  node scripts/check-latest-card-art.mjs

  [[ "$need_npm" == 1 || ! -d node_modules ]] && npm ci
  rm -rf .next.previous
  [[ -d .next ]] && mv .next .next.previous
  if NEXT_PUBLIC_WS_URL='wss://test.grand-umi.com/ws' npm run build; then
    systemctl restart grandumi-test-frontend.service
    systemctl is-active --quiet grandumi-test-frontend.service || die "测试服前端启动失败。"
    rm -rf .next.previous
  else
    rm -rf .next
    [[ -d .next.previous ]] && mv .next.previous .next
    systemctl restart grandumi-test-frontend.service || true
    die "测试服前端构建失败，已回滚。"
  fi
fi

sleep 3
backend_ready
curl -fsS --retry 5 --retry-delay 1 -o /dev/null http://127.0.0.1:3001/
echo "$target" > "$state_dir/test-deployed.next"
mv "$state_dir/test-deployed.next" "$state_dir/test-deployed"
echo "测试服部署成功：$target"
