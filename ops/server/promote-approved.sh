#!/usr/bin/env bash
set -Eeuo pipefail

repo=/opt/grandumi
state_dir=/var/lib/grandumi-release
approved_file="$state_dir/approved"
test_file="$state_dir/test-deployed"
deployed_file="$state_dir/production-deployed"

log() {
  echo "[$(date '+%F %T')] $*"
}

die() {
  log "错误：$*" >&2
  exit 1
}

backend_ready() {
  curl -fsS --retry 10 --retry-delay 1 --retry-connrefused \
    -o /dev/null http://127.0.0.1:8080/ready
}

backend_matches_approved() {
  curl -fsS http://127.0.0.1:8080/version | grep -Fq "$approved"
}

[[ -s "$approved_file" ]] || { log "没有已批准版本，本次不发布。"; exit 0; }
[[ -s "$test_file" ]] || die "没有测试服部署记录。"
approved="$(tr -d '\r\n' < "$approved_file")"
tested="$(tr -d '\r\n' < "$test_file")"
[[ "$approved" =~ ^[0-9a-f]{40}$ ]] || die "批准记录格式无效。"
[[ "$approved" == "$tested" ]] || die "已批准版本与当前测试服版本不一致。"

# 最长等待一小时；只要仍有经过 Caddy 的 WebSocket 连接，就不重启正式服。
for attempt in {1..13}; do
  connections="$(ss -Hnt state established '( sport = :8080 )' | wc -l)"
  if [[ "$connections" -eq 0 ]]; then
    break
  fi
  if [[ "$attempt" -eq 13 ]]; then
    log "正式服仍有 $connections 个 WebSocket 连接，跳过本次发布。"
    exit 0
  fi
  log "正式服仍有 $connections 个连接，5 分钟后重试（$attempt/12）。"
  sleep 300
done

generated='opcgpro-web/src/data/dataVersion.ts'
dirty="$(git -C "$repo" -c core.quotepath=false diff --name-only |
  grep -Fvx "$generated" |
  grep -v '^服务端WebSocket/publish/' || true)"
[[ -z "$dirty" ]] || die "正式服存在未知受控文件改动，拒绝发布：$dirty"
git -C "$repo" restore --worktree --staged -- "$generated" 2>/dev/null || true
git -C "$repo" restore --worktree --staged -- '服务端WebSocket/publish' 2>/dev/null || true

log "获取已批准提交 $approved"
git -C "$repo" fetch origin main
git -C "$repo" cat-file -e "$approved^{commit}" 2>/dev/null || die "正式服仓库中不存在已批准提交。"
git -C "$repo" merge-base --is-ancestor "$approved" origin/main || die "已批准提交不属于 origin/main。"

old="$(cat "$deployed_file" 2>/dev/null || git -C "$repo" rev-parse HEAD)"
git -C "$repo" merge-base --is-ancestor "$old" "$approved" || die "批准版本不是正式服版本的后继提交。"
[[ "$old" != "$approved" ]] || { log "正式服已经是该版本。"; exit 0; }
changed="$(git -C "$repo" -c core.quotepath=false diff --name-only "$old" "$approved")"

# 仅快进，不覆盖未知本地数据。
git -C "$repo" merge --ff-only "$approved"

# 正式服静态资源独立使用 Cloudflare，同时持久化源站出口保护，避免重启后配置丢失。
install -m 0644 "$repo/ops/server/assets.grand-umi.com.caddy" \
  /etc/caddy/conf.d/assets.grand-umi.com.caddy
install -m 0644 "$repo/ops/server/60-grandumi-network.conf" \
  /etc/sysctl.d/60-grandumi-network.conf
install -m 0644 "$repo/ops/server/grandumi-network-modules.conf" \
  /etc/modules-load.d/grandumi-network.conf
install -m 0755 "$repo/ops/server/apply-grandumi-network.sh" \
  /usr/local/sbin/apply-grandumi-network
install -m 0644 "$repo/ops/server/grandumi-network-tuning.service" \
  /etc/systemd/system/grandumi-network-tuning.service
systemctl daemon-reload
modprobe tcp_bbr
modprobe sch_fq
modprobe sch_htb
sysctl --system >/dev/null
systemctl enable grandumi-network-tuning.service
systemctl restart grandumi-network-tuning.service
caddy validate --config /etc/caddy/Caddyfile

need_back=0
need_front=0
need_npm=0
grep -q '^服务端WebSocket/' <<<"$changed" && need_back=1 || true
grep -q '^opcgpro-web/' <<<"$changed" && need_front=1 || true
grep -Eq '^opcgpro-web/(package|package-lock)\.json$' <<<"$changed" && need_npm=1 || true

next_publish="$repo/服务端WebSocket/publish.next"
if [[ "$need_back" == 1 ]]; then
  log "在临时目录构建正式服后端"
  rm -rf "$next_publish"
  dotnet publish "$repo/服务端WebSocket/GrandUMIServer.csproj" -c Release -o "$next_publish" --nologo \
    -p:InformationalVersion="1.0.0+$approved" \
    -p:IncludeSourceRevisionInInformationalVersion=false

  log "增量回填正式服 Leader 排行榜数据"
  production_stats_db="$repo/服务端WebSocket/Data/leader-stats.db"
  mkdir -p "$(dirname "$production_stats_db")"
  dotnet "$next_publish/GrandUMIServer.dll" \
    --backfill-leader-stats \
    "$repo/服务端WebSocket/MatchLogs" \
    "$production_stats_db"
fi

if [[ "$need_front" == 1 ]]; then
  log "构建正式服前端并保留旧构建"
  cd "$repo/opcgpro-web"
  [[ "$need_npm" == 1 || ! -d node_modules ]] && npm ci
  rm -rf .next.previous
  [[ -d .next ]] && mv .next .next.previous
  if ! NEXT_PUBLIC_WS_URL='wss://grand-umi.com/ws' \
      NEXT_PUBLIC_ASSET_ORIGIN='https://assets.grand-umi.com' \
      CARD_BACK_API_URL='http://127.0.0.1:8080' npm run build; then
    rm -rf .next
    [[ -d .next.previous ]] && mv .next.previous .next
    die "正式服前端构建失败，旧服务保持运行。"
  fi
fi

if [[ "$need_back" == 1 ]]; then
  previous_publish="$repo/服务端WebSocket/publish.previous"
  rm -rf "$previous_publish"
  [[ -d "$repo/服务端WebSocket/publish" ]] && mv "$repo/服务端WebSocket/publish" "$previous_publish"
  mv "$next_publish" "$repo/服务端WebSocket/publish"
  if ! systemctl restart grandumi-backend.service \
      || ! systemctl is-active --quiet grandumi-backend.service \
      || ! backend_ready \
      || ! backend_matches_approved; then
    rm -rf "$repo/服务端WebSocket/publish"
    [[ -d "$previous_publish" ]] && mv "$previous_publish" "$repo/服务端WebSocket/publish"
    systemctl restart grandumi-backend.service || true
    backend_ready || true
    die "正式服后端启动或就绪检查失败，已尝试回滚。"
  fi
fi

if [[ "$need_front" == 1 ]]; then
  if ! systemctl restart grandumi-frontend.service || ! systemctl is-active --quiet grandumi-frontend.service; then
    rm -rf "$repo/opcgpro-web/.next"
    [[ -d "$repo/opcgpro-web/.next.previous" ]] && mv "$repo/opcgpro-web/.next.previous" "$repo/opcgpro-web/.next"
    systemctl restart grandumi-frontend.service || true
    die "正式服前端启动失败，已尝试回滚。"
  fi
  rm -rf "$repo/opcgpro-web/.next.previous"
fi

systemctl start grandumi-backend.service
systemctl start grandumi-frontend.service
caddy validate --config /etc/caddy/Caddyfile
if systemctl is-active --quiet caddy; then
  systemctl reload caddy
else
  systemctl start caddy
fi

sleep 3
backend_ready
curl -fsS --retry 5 --retry-delay 1 -o /dev/null http://127.0.0.1:3000/
curl -kfsS --retry 5 --retry-delay 1 --resolve assets.grand-umi.com:443:127.0.0.1 \
  -o /dev/null https://assets.grand-umi.com/sprites-thumb/CardBack.webp
echo "$approved" > "$deployed_file.next"
mv "$deployed_file.next" "$deployed_file"
rm -f "$approved_file"
log "正式服发布成功：$approved"
