#!/usr/bin/env bash
set -Eeuo pipefail

repo=/opt/grandumi-test
state_dir=/var/lib/grandumi-test-release
deploy_lock=/run/lock/grandumi-test-deploy.lock
account_cutover_lock=/run/lock/grandumi-account-authority-cutover.lock
replay_archive_root=/var/lib/grandumi-test-replay-artifacts
replay_archive_env=/etc/grandumi/grandumi-test-replay-artifact.env
target="$1"
force="${2:-}"
verification_proof="${3:-}"
verification_checksum="${4:-}"

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
expected_proof="/tmp/grandumi-test-${target:0:12}.verify.json"
[[ "$verification_proof" == "$expected_proof" ]] || die "验证证明路径与待部署提交不匹配。"
[[ "$verification_checksum" =~ ^[0-9a-f]{64}$ ]] || die "验证证明 SHA-256 格式无效。"
[[ -f "$verification_proof" ]] || die "缺少测试服验证证明。"
verification_next=""
cleanup_verification() {
  rm -f "$verification_proof"
  [[ -z "$verification_next" ]] || rm -f "$verification_next"
}
trap cleanup_verification EXIT

mkdir -p "$state_dir"
exec 9>"$deploy_lock"
flock -n 9 || die "另一个测试服部署正在进行，拒绝并发执行"
repo_head="$(git -C "$repo" rev-parse HEAD)"

# 仓库 HEAD 可能已被一次失败的门禁或构建提前移动，不能代表实际运行版本。
# 只有最后一次原子写入的 test-deployed 才能作为增量部署基线；任何不可信状态都保守全量部署。
deployment_state="$state_dir/test-deployed"
deployment_base=""
changed=""
full_deploy=0
full_deploy_reason=""
require_full_deploy() {
  full_deploy=1
  full_deploy_reason="$1"
  changed=""
}

if [[ "$force" == "all" ]]; then
  require_full_deploy "发布入口要求全量部署"
elif [[ ! -f "$deployment_state" ]]; then
  require_full_deploy "缺少 test-deployed 成功状态"
elif ! deployment_base="$(cat "$deployment_state" 2>/dev/null)"; then
  require_full_deploy "无法读取 test-deployed 成功状态"
else
  deployment_base="${deployment_base%$'\r'}"
  if [[ ! "$deployment_base" =~ ^[0-9a-f]{40}$ ]]; then
    require_full_deploy "test-deployed 成功状态格式非法"
  elif ! git -C "$repo" cat-file -e "$deployment_base^{commit}" 2>/dev/null; then
    require_full_deploy "test-deployed 提交对象不可用"
  elif ! git -C "$repo" merge-base --is-ancestor "$deployment_base" "$target" 2>/dev/null; then
    require_full_deploy "test-deployed 不是待部署提交的祖先"
  elif [[ "$deployment_base" == "$target" ]]; then
    require_full_deploy "待部署提交已标记成功，执行确定性全量重建"
  elif ! changed="$(git -C "$repo" -c core.quotepath=false diff --name-only "$deployment_base" "$target" 2>/dev/null)"; then
    require_full_deploy "无法比较 test-deployed 与待部署提交"
  fi
fi

# prebuild 会更新这个生成文件；除此以外的受控文件改动都必须人工处理。
generated='opcgpro-web/src/data/dataVersion.ts'
dirty="$(git -C "$repo" -c core.quotepath=false diff --name-only |
  grep -Fvx "$generated" |
  grep -v '^服务端WebSocket/publish/' || true)"
[[ -z "$dirty" ]] || die "测试服存在未知受控文件改动：$dirty"
git -C "$repo" restore --worktree --staged -- "$generated" 2>/dev/null || true
git -C "$repo" restore --worktree --staged -- '服务端WebSocket/publish' 2>/dev/null || true

git -C "$repo" checkout --detach "$target"
target_tree="$(git -C "$repo" rev-parse "$target^{tree}")"
node "$repo/tools/verification-proof.mjs" verify \
  --proof "$verification_proof" \
  --commit "$target" \
  --tree "$target_tree" \
  --checksum "$verification_checksum" \
  || die "测试服验证证明无效。"
verification_next="$state_dir/test-verified.next.$$"
install -m 0644 "$verification_proof" "$verification_next"
install -d -m 0755 /var/lib/grandumi-admin-deploy/status
install -d -o grandumi -g grandumi -m 0750 /var/lib/grandumi-admin-deploy/requests
install -d -o grandumi -g grandumi -m 0750 /var/lib/grandumi-admin-deploy/drafts
install -d -o root -g grandumi -m 0750 /data/grandumi-test/hex-catalog
install -m 0755 "$repo/ops/server/grandumi-admin-deploy.sh" /usr/local/sbin/grandumi-admin-deploy
install -m 0644 "$repo/ops/server/grandumi-admin-deploy.service" /etc/systemd/system/grandumi-admin-deploy.service
install -m 0644 "$repo/ops/server/grandumi-admin-deploy.path" /etc/systemd/system/grandumi-admin-deploy.path
systemctl daemon-reload
systemctl enable --now grandumi-admin-deploy.path
need_back=0
need_front=0
need_npm=0
if [[ "$full_deploy" == 1 ]]; then
  need_back=1
  need_front=1
  need_npm=1
fi
grep -q '^服务端WebSocket/' <<<"$changed" && need_back=1 || true
grep -q '^opcgpro-web/' <<<"$changed" && need_front=1 || true
grep -Eq '^opcgpro-web/(package|package-lock)\.json$' <<<"$changed" && need_npm=1 || true

echo "测试服仓库 HEAD：$(git -C "$repo" rev-parse --short=12 "$repo_head") -> $(git -C "$repo" rev-parse --short=12 "$target")"
if [[ "$full_deploy" == 1 ]]; then
  echo "测试服部署基线：$full_deploy_reason；执行前后端全量部署"
else
  echo "测试服部署基线：$(git -C "$repo" rev-parse --short=12 "$deployment_base") -> $(git -C "$repo" rev-parse --short=12 "$target")"
fi

if [[ "$need_back" == 1 ]]; then
  exec 8>"$account_cutover_lock"
  flock -n 8 || die "共享账号权威切换正在进行，拒绝并发重启测试后端"
  echo "构建测试服后端"
  next_publish="$repo/服务端WebSocket/publish.next"
  previous_publish="$repo/服务端WebSocket/publish.previous"
  rm -rf "$next_publish"
  /opt/dotnet/dotnet publish "$repo/服务端WebSocket/GrandUMIServer.csproj" -c Release -o "$next_publish" --nologo \
    -p:InformationalVersion="1.0.0+$target" \
    -p:IncludeSourceRevisionInInformationalVersion=false
  [[ -f "$next_publish/.grandumi-shared-account-v1" ]] \
    || die "测试服后端发布包缺少共享账号兼容标记"

  # 玩法资料仍只写 /data/grandumi-test。此处只准备共享目录，
  # 绝不读取或迁移正式 players.db，也绝不创建 active 激活标记。
  install -d -o grandumi -g grandumi -m 0750 /data/grandumi-test
  install -d -o grandumi -g grandumi -m 0750 /data/grandumi-shared
  install -d -o grandumi -g grandumi -m 0750 /data/grandumi-test/Rulesets

  # 归档根只服务测试服；完整 publish 与规则包先在同卷 .staging 中复制、逐字节校验，
  # 再由归档命令用目录 rename 发布。任何同 artifactId 冲突都会在切换服务前失败。
  install -d -o root -g grandumi -m 2750 "$replay_archive_root"
  install -d -o root -g grandumi -m 2750 "$replay_archive_root/.staging"
  capture_output="$({
    /opt/dotnet/dotnet "$next_publish/GrandUMIServer.dll" --replay-artifact capture \
      --publish-root "$next_publish" \
      --rules-root /data/grandumi-test/Rulesets \
      --archive-root "$replay_archive_root" \
      --engine-commit "$target"
  } | tee /dev/stderr)"
  capture_line="$(tail -n 1 <<<"$capture_output")"
  IFS=$'\t' read -r capture_marker replay_artifact_id replay_manifest replay_capture_disposition capture_extra \
    <<<"$capture_line"
  [[ "$capture_marker" == "REPLAY_ARTIFACT" ]] \
    || die "重放工件归档没有返回受控结果。"
  [[ "$replay_artifact_id" =~ ^grandumi-runtime-[0-9a-f]{64}$ ]] \
    || die "重放工件 artifactId 格式无效。"
  [[ "$replay_manifest" == "$replay_archive_root/$replay_artifact_id/replay-artifact-manifest.v1.json" ]] \
    || die "重放工件 manifest 路径不在预期的测试服归档目录。"
  [[ -z "${capture_extra:-}" ]] || die "重放工件归档结果包含额外字段。"
  /opt/dotnet/dotnet "$next_publish/GrandUMIServer.dll" --replay-artifact verify \
    --archive "$replay_manifest" \
    --dotnet /opt/dotnet/dotnet

  install -m 0644 "$repo/ops/server/grandumi-test-backend.service" \
    /etc/systemd/system/grandumi-test-backend.service
  install -d -m 0755 /etc/grandumi
  install -m 0644 "$repo/ops/server/grandumi-qq-whitelist-sync.env.example" \
    /etc/grandumi/qq-whitelist-sync.env.example
  systemctl daemon-reload

  replay_env_backup="$state_dir/replay-artifact-env.previous.$$"
  rm -f "$replay_env_backup"
  replay_env_existed=0
  if [[ -f "$replay_archive_env" ]]; then
    cp -p "$replay_archive_env" "$replay_env_backup"
    replay_env_existed=1
  fi
  replay_env_next="$replay_archive_env.next"
  {
    echo "GRANDUMI_REPLAY_ARTIFACT_MANIFEST=$replay_manifest"
  } > "$replay_env_next"
  chmod 0644 "$replay_env_next"

  rm -rf "$previous_publish"
  if [[ -d "$repo/服务端WebSocket/publish" ]] \
      && ! mv "$repo/服务端WebSocket/publish" "$previous_publish"; then
    rm -f "$replay_env_next" "$replay_env_backup"
    die "无法保存上一版测试服后端发布目录，尚未切换服务。"
  fi
  if ! mv "$next_publish" "$repo/服务端WebSocket/publish"; then
    [[ -d "$previous_publish" ]] && mv "$previous_publish" "$repo/服务端WebSocket/publish"
    rm -f "$replay_env_next" "$replay_env_backup"
    die "无法原子切换测试服后端发布目录，已尝试恢复上一版。"
  fi
  if ! mv "$replay_env_next" "$replay_archive_env"; then
    rm -rf "$repo/服务端WebSocket/publish"
    [[ -d "$previous_publish" ]] && mv "$previous_publish" "$repo/服务端WebSocket/publish"
    rm -f "$replay_env_next" "$replay_env_backup"
    die "无法切换测试服重放归档环境文件，已尝试恢复上一版后端。"
  fi
  if ! systemctl enable grandumi-test-backend.service \
      || ! systemctl restart grandumi-test-backend.service \
      || ! systemctl is-active --quiet grandumi-test-backend.service \
      || ! backend_ready \
      || ! backend_matches_target \
      || ! /opt/dotnet/dotnet "$repo/服务端WebSocket/publish/GrandUMIServer.dll" --replay-artifact audit \
        --logs /data/grandumi-test/MatchLogs \
        --archive-root "$replay_archive_root" \
        --json "$state_dir/replay-coverage.v1.json" \
        --markdown "$state_dir/replay-coverage.v1.md" \
        --candidate-catalog "$state_dir/test-replay-artifact-candidates.v1.json" \
        --dotnet /opt/dotnet/dotnet; then
    rm -rf "$repo/服务端WebSocket/publish"
    [[ -d "$previous_publish" ]] && mv "$previous_publish" "$repo/服务端WebSocket/publish"
    if [[ "$replay_env_existed" == 1 ]]; then
      mv "$replay_env_backup" "$replay_archive_env"
    else
      rm -f "$replay_archive_env" "$replay_env_backup"
    fi
    systemctl restart grandumi-test-backend.service || true
    backend_ready || true
    die "测试服后端启动、归档绑定或覆盖审计失败，已尝试回滚。"
  fi
  rm -f "$replay_env_backup"
fi

if [[ "$need_front" == 1 ]]; then
  echo "构建测试服前端"
  cd "$repo/opcgpro-web"

  # 卡图派生资源不进入 Git。每次前端部署先从正式服的只读资源库增量补齐测试服，
  # 再核对 manifest 中每张多画卡的最新异画，避免清单已更新但图鉴只能回退到正画。
  production_assets=/opt/grandumi/opcgpro-web/public
  test_assets=/opt/grandumi-test-assets

  # Windows 开发目录中的 public/cards 是指向 CardImages 的 junction，不会进入 Git。
  # 新服务器从现有正式卡图库复制到独立测试资源目录，再建立只供测试前端使用的链接。
  original_cards_source=/opt/grandumi/opcgpro-vue/public/cards
  original_cards_target="$test_assets/cards"
  [[ -d "$original_cards_source" ]] || die "正式服原始卡图目录不存在：$original_cards_source"
  mkdir -p "$original_cards_target"
  rsync -au "$original_cards_source/" "$original_cards_target/"
  public_cards_link="$repo/opcgpro-web/public/cards"
  if [[ -e "$public_cards_link" && ! -L "$public_cards_link" ]]; then
    rsync -au "$public_cards_link/" "$original_cards_target/"
    rm -rf "$public_cards_link"
  fi
  ln -sfn "$original_cards_target" "$public_cards_link"

  for asset_dir in cards-thumb cards-webp; do
    source_dir="$production_assets/$asset_dir"
    target_dir="$test_assets/$asset_dir"
    [[ -d "$source_dir" ]] || die "正式服卡图资源目录不存在：$source_dir"
    mkdir -p "$target_dir"
    # 测试服可能先行验证本机补齐的资源；保留目标端时间更新的修正版，正式资源更新后仍可继续增量同步。
    rsync -au "$source_dir/" "$target_dir/"
    # 构建与运行时均通过测试服自己的链接读取资源，不修改正式服资源目录。
    public_link="$repo/opcgpro-web/public/$asset_dir"
    if [[ -e "$public_link" && ! -L "$public_link" ]]; then
      rsync -au "$public_link/" "$target_dir/"
      rm -rf "$public_link"
    fi
    ln -sfn "$target_dir" "$public_link"
  done
  [[ "$need_npm" == 1 || ! -d node_modules ]] && npm ci --no-audit --no-fund
  node scripts/check-latest-card-art.mjs
  if ! node scripts/check-card-image-assets.mjs; then
    echo "测试服派生卡图需要刷新，按原始卡图增量重新生成"
    npm run gen:card-thumbs
    node scripts/check-card-image-assets.mjs
  fi
  rm -rf .next.previous
  [[ -d .next ]] && mv .next .next.previous
  if NEXT_PUBLIC_WS_URL='wss://test.grand-umi.com/ws' \
      NEXT_PUBLIC_ASSET_ORIGIN='https://test.grand-umi.com' \
      NEXT_PUBLIC_GRANDUMI_COMMIT="$target" \
      CARD_BACK_API_URL='http://127.0.0.1:8081' npm run build; then
    chown -R grandumi:grandumi .next
    install -m 0644 "$repo/ops/server/grandumi-test-frontend.service" \
      /etc/systemd/system/grandumi-test-frontend.service
    systemctl daemon-reload
    systemctl enable grandumi-test-frontend.service
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

# 首次迁移先安装仅含 HTTP 与 ACME 挑战的站点；DNS 切换并签发证书后再启用 TLS 配置。
if [[ -f /etc/letsencrypt/live/test.grand-umi.com/fullchain.pem ]]; then
  install -m 0644 "$repo/ops/server/grandumi-test.nginx" /etc/nginx/sites-available/grandumi-test
  ln -sfn /etc/nginx/sites-available/grandumi-test /etc/nginx/sites-enabled/grandumi-test
  nginx -t
  systemctl reload nginx
elif [[ ! -e /etc/nginx/sites-enabled/grandumi-test ]]; then
  install -m 0644 "$repo/ops/server/grandumi-test-acme.nginx" /etc/nginx/sites-available/grandumi-test
  ln -s /etc/nginx/sites-available/grandumi-test /etc/nginx/sites-enabled/grandumi-test
  nginx -t
  systemctl reload nginx
fi

backend_ready
curl -fsS --retry 10 --retry-delay 1 --retry-connrefused -o /dev/null http://127.0.0.1:3001/
echo "$target" > "$state_dir/test-deployed.next"
mv "$verification_next" "$state_dir/test-verified.json"
verification_next=""
mv "$state_dir/test-deployed.next" "$state_dir/test-deployed"
echo "测试服部署成功：$target"
