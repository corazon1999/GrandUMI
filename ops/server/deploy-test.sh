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
  dotnet publish "$repo/服务端WebSocket/GrandUMIServer.csproj" -c Release -o "$next_publish" --nologo
  rm -rf "$previous_publish"
  [[ -d "$repo/服务端WebSocket/publish" ]] && mv "$repo/服务端WebSocket/publish" "$previous_publish"
  mv "$next_publish" "$repo/服务端WebSocket/publish"
  if ! systemctl restart grandumi-test-backend.service || ! systemctl is-active --quiet grandumi-test-backend.service; then
    rm -rf "$repo/服务端WebSocket/publish"
    [[ -d "$previous_publish" ]] && mv "$previous_publish" "$repo/服务端WebSocket/publish"
    systemctl restart grandumi-test-backend.service || true
    die "测试服后端启动失败，已尝试回滚。"
  fi
fi

if [[ "$need_front" == 1 ]]; then
  echo "构建测试服前端"
  cd "$repo/opcgpro-web"
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
curl -fsS --retry 5 --retry-delay 1 -o /dev/null http://127.0.0.1:3001/
echo "$target" > "$state_dir/test-deployed.next"
mv "$state_dir/test-deployed.next" "$state_dir/test-deployed"
echo "测试服部署成功：$target"
