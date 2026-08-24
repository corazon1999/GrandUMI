import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const [entry, deploy, backendService, frontendService, acmeNginx, tlsNginx, enableTls] = await Promise.all([
  readFile(new URL("../../deploy-test.ps1", import.meta.url), "utf8"),
  readFile(new URL("../../ops/server/deploy-test.sh", import.meta.url), "utf8"),
  readFile(new URL("../../ops/server/grandumi-test-backend.service", import.meta.url), "utf8"),
  readFile(new URL("../../ops/server/grandumi-test-frontend.service", import.meta.url), "utf8"),
  readFile(new URL("../../ops/server/grandumi-test-acme.nginx", import.meta.url), "utf8"),
  readFile(new URL("../../ops/server/grandumi-test.nginx", import.meta.url), "utf8"),
  readFile(new URL("../../ops/server/enable-grandumi-test-tls.sh", import.meta.url), "utf8"),
]);

test("测试服部署入口默认指向香港新服务器并支持首次初始化", () => {
  assert.match(entry, /root@103\.146\.230\.37/);
  assert.doesNotMatch(entry, /8\.210\.155\.25/);
  assert.match(entry, /git -C \/opt\/grandumi-test init/);
  assert.match(entry, /-not \$hasServerHead/);
  assert.match(entry, /git -C \/opt\/grandumi-test show '\$target`:ops\/server\/deploy-test\.sh' \| bash -s --/);
  assert.doesNotMatch(entry, /git add -A/);
});

test("测试服数据、端口与进程权限均和正式服隔离", () => {
  assert.match(backendService, /User=grandumi/);
  assert.match(backendService, /GRANDUMI_DATA_DIR=\/data\/grandumi-test/);
  assert.match(backendService, /GrandUMIServer\.dll 8081/);
  assert.doesNotMatch(backendService, /\/data\/grandumi(?:\s|$)/m);
  assert.doesNotMatch(backendService, /GRANDUMI_PLAYER_DB/);
  assert.match(frontendService, /User=grandumi/);
  assert.match(frontendService, /127\.0\.0\.1 -p 3001/);
  assert.match(deploy, /install -d -o grandumi -g grandumi -m 0750 \/data\/grandumi-test/);
  assert.match(deploy, /original_cards_source=\/opt\/grandumi\/opcgpro-vue\/public\/cards/);
  assert.match(deploy, /original_cards_target="\$test_assets\/cards"/);
  assert.match(deploy, /ln -sfn "\$original_cards_target" "\$public_cards_link"/);
  assert.match(deploy, /if ! node scripts\/check-card-image-assets\.mjs/);
  assert.match(deploy, /npm run gen:card-thumbs/);
  assert.doesNotMatch(deploy, /production_stats_db|--backfill-leader-stats/);
});

test("测试域名先支持 ACME，再以 HTTPS 分流前端与 WebSocket", () => {
  assert.match(acmeNginx, /server_name test\.grand-umi\.com/);
  assert.match(acmeNginx, /\.well-known\/acme-challenge/);
  assert.match(acmeNginx, /127\.0\.0\.1:3001/);
  assert.match(tlsNginx, /ssl_certificate \/etc\/letsencrypt\/live\/test\.grand-umi\.com\/fullchain\.pem/);
  assert.match(tlsNginx, /proxy_pass http:\/\/127\.0\.0\.1:8081\/ws/);
  assert.match(tlsNginx, /proxy_pass http:\/\/127\.0\.0\.1:3001/);
  assert.match(enableTls, /certbot certonly --webroot/);
  assert.match(enableTls, /--resolve "\$domain:443:127\.0\.0\.1"/);
});
