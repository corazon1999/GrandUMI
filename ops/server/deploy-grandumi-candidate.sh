#!/usr/bin/env bash
set -Eeuo pipefail

repo=/opt/grandumi-candidate
target="${1:-}"
candidate_ip="${GRANDUMI_CANDIDATE_IP:-103.146.230.37}"
candidate_host="${GRANDUMI_CANDIDATE_HOST:-candidate.grand-umi.com}"
candidate_asset_origin="${GRANDUMI_CANDIDATE_ASSET_ORIGIN:-https://$candidate_host}"

die() { echo "错误：$*" >&2; exit 1; }
[[ "$candidate_ip" == "103.146.230.37" ]] || die "拒绝部署到未登记主机：$candidate_ip"
[[ "$candidate_asset_origin" == https://* ]] || die "候选服静态资源地址必须使用 HTTPS"
[[ "$target" =~ ^[0-9a-f]{40}$ ]] || die "必须提供 40 位提交号"
git -C "$repo" cat-file -e "$target^{commit}" 2>/dev/null || die "候选服仓库中不存在提交 $target"

git -C "$repo" restore --worktree --staged -- opcgpro-web/src/data/dataVersion.ts opcgpro-web/public/network-endpoints.json 2>/dev/null || true
git -C "$repo" checkout --detach "$target"

backend_next="$repo/服务端WebSocket/publish.next"
backend_previous="$repo/服务端WebSocket/publish.previous"
rm -rf "$backend_next"
dotnet publish "$repo/服务端WebSocket/GrandUMIServer.csproj" -c Release -o "$backend_next" --nologo \
  -p:InformationalVersion="1.0.0+$target" \
  -p:IncludeSourceRevisionInInformationalVersion=false

cd "$repo/opcgpro-web"
npm ci --no-audit --no-fund
rm -rf .next.candidate-previous
[[ -d .next ]] && mv .next .next.candidate-previous
if ! NEXT_PUBLIC_WS_URL="wss://$candidate_host/ws" \
    NEXT_PUBLIC_ASSET_ORIGIN="$candidate_asset_origin" \
    CARD_BACK_API_URL=http://127.0.0.1:18080 npm run build; then
  rm -rf .next
  [[ -d .next.candidate-previous ]] && mv .next.candidate-previous .next
  die "前端构建失败"
fi
cat > public/network-endpoints.json <<JSON
{"version":1,"hosts":["$candidate_host"],"endpoints":[{"url":"wss://$candidate_host/ws","enabled":true}]}
JSON

rm -rf "$backend_previous"
[[ -d "$repo/服务端WebSocket/publish" ]] && mv "$repo/服务端WebSocket/publish" "$backend_previous"
mv "$backend_next" "$repo/服务端WebSocket/publish"
chown -R grandumi:grandumi /data/grandumi-candidate "$repo/服务端WebSocket/publish" "$repo/opcgpro-web/.next"

install -m 0644 "$repo/ops/server/grandumi-candidate-backend.service" /etc/systemd/system/grandumi-candidate-backend.service
install -m 0644 "$repo/ops/server/grandumi-candidate-frontend.service" /etc/systemd/system/grandumi-candidate-frontend.service
install -m 0644 "$repo/ops/server/grandumi-candidate.nginx" /etc/nginx/sites-available/grandumi-candidate
if [[ -f "/etc/letsencrypt/live/$candidate_host/fullchain.pem" ]]; then
  install -m 0644 "$repo/ops/server/grandumi-candidate-tls.nginx" /etc/nginx/sites-available/grandumi-candidate
fi
systemctl daemon-reload
nginx -t
systemctl restart grandumi-candidate-backend.service
curl -fsS --retry 20 --retry-delay 1 --retry-connrefused http://127.0.0.1:18080/ready >/dev/null
curl -fsS http://127.0.0.1:18080/version | grep -Fq "$target" || die "后端版本与目标提交不一致"
systemctl restart grandumi-candidate-frontend.service nginx
curl -fsS --retry 20 --retry-delay 1 --retry-connrefused http://127.0.0.1:13000/ >/dev/null
rm -rf .next.candidate-previous "$backend_previous"

echo "候选服部署成功：$target"
