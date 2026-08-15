import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const stage = await readFile(new URL("../../ops/server/stage-grandumi-production.sh", import.meta.url), "utf8");
const activate = await readFile(new URL("../../ops/server/activate-grandumi-production.sh", import.meta.url), "utf8");
const nginx = await readFile(new URL("../../ops/server/grandumi-production.nginx", import.meta.url), "utf8");
const backendService = await readFile(new URL("../../ops/server/grandumi-production-backend.service", import.meta.url), "utf8");
const deploy = await readFile(new URL("../../deploy-new-hk-production.ps1", import.meta.url), "utf8");

test("新正式服预构建固定使用正式 HTTPS/WSS 域名", () => {
  assert.match(stage, /NEXT_PUBLIC_WS_URL='wss:\/\/grand-umi\.com\/ws'/);
  assert.match(stage, /NEXT_PUBLIC_ASSET_ORIGIN='https:\/\/grand-umi\.com'/);
  assert.match(stage, /"grand-umi\.com","candidate\.grand-umi\.com"/);
  assert.match(stage, /尚未切换服务/);
});

test("双域名入口分别使用正确证书并共享反向代理", () => {
  assert.match(nginx, /server_name grand-umi\.com;/);
  assert.match(nginx, /live\/grand-umi\.com\/fullchain\.pem/);
  assert.match(nginx, /server_name candidate\.grand-umi\.com;/);
  assert.match(nginx, /live\/candidate\.grand-umi\.com\/fullchain\.pem/);
  assert.equal((nginx.match(/grandumi-production-proxy\.conf/g) ?? []).length, 2);
});

test("正式数据未就绪时拒绝激活，失败时恢复候选服务", () => {
  assert.match(activate, /import_dir=\/data\/grandumi-import\/final/);
  assert.match(activate, /\[\[ -f "\$import_dir\/\.ready" \]\]/);
  assert.match(activate, /PRAGMA integrity_check/);
  assert.match(activate, /rollback\(\)/);
  assert.match(activate, /systemctl start grandumi-candidate-backend\.service grandumi-candidate-frontend\.service/);
  assert.match(backendService, /GRANDUMI_NODE_ID=hk-production-01/);
});

test("Windows 部署入口只允许新正式服 IP 且仅做预构建", () => {
  assert.match(deploy, /root@103\.146\.230\.37/);
  assert.doesNotMatch(deploy, /8\.210\.155\.25/);
  assert.match(deploy, /stage-grandumi-production\.sh/);
  assert.match(deploy, /尚未切流/);
});
