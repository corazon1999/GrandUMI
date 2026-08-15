#!/usr/bin/env bash
set -Eeuo pipefail

repo=/opt/grandumi
target="${1:-}"
production_ip="${GRANDUMI_PRODUCTION_IP:-103.146.230.37}"

die() { echo "错误：$*" >&2; exit 1; }
[[ "$production_ip" == "103.146.230.37" ]] || die "拒绝部署到未登记主机：$production_ip"
[[ "$target" =~ ^[0-9a-f]{40}$ ]] || die "必须提供 40 位提交号"
git -C "$repo" cat-file -e "$target^{commit}" 2>/dev/null || die "新正式服仓库中不存在提交 $target"

git -C "$repo" restore --worktree --staged -- opcgpro-web/src/data/dataVersion.ts opcgpro-web/public/network-endpoints.json 2>/dev/null || true
git -C "$repo" checkout --detach "$target"

publish_next="$repo/服务端WebSocket/publish.next"
rm -rf "$publish_next"
dotnet publish "$repo/服务端WebSocket/GrandUMIServer.csproj" -c Release -o "$publish_next" --nologo \
  -p:InformationalVersion="1.0.0+$target" \
  -p:IncludeSourceRevisionInInformationalVersion=false

cd "$repo/opcgpro-web"
npm ci --no-audit --no-fund
rm -rf .next.production-previous
[[ -d .next ]] && mv .next .next.production-previous
if ! NEXT_PUBLIC_WS_URL='wss://grand-umi.com/ws' \
    NEXT_PUBLIC_ASSET_ORIGIN='https://grand-umi.com' \
    CARD_BACK_API_URL=http://127.0.0.1:8080 npm run build; then
  rm -rf .next
  [[ -d .next.production-previous ]] && mv .next.production-previous .next
  die "新正式服前端构建失败"
fi
cat > public/network-endpoints.json <<'JSON'
{"version":1,"hosts":["grand-umi.com","candidate.grand-umi.com"],"endpoints":[{"url":"wss://grand-umi.com/ws","enabled":true},{"url":"wss://candidate.grand-umi.com/ws","enabled":true}]}
JSON

rm -rf "$repo/服务端WebSocket/publish"
mv "$publish_next" "$repo/服务端WebSocket/publish"
rm -rf .next.production-previous
chown -R grandumi:grandumi "$repo/服务端WebSocket/publish" "$repo/opcgpro-web/.next"

install -m 0644 "$repo/ops/server/grandumi-production-backend.service" /etc/systemd/system/grandumi-production-backend.service
install -m 0644 "$repo/ops/server/grandumi-production-frontend.service" /etc/systemd/system/grandumi-production-frontend.service
systemctl daemon-reload

printf '%s\n' "$target" > /var/lib/grandumi-production-staged
echo "新正式服版本已预构建，尚未切换服务：$target"
