# 测试服迁移至香港服务器

- 日期：2026-08-25
- 分类：优化
- 影响范围：测试服部署、登录与对局连接、卡图资源、HTTPS
- 状态：已完成

## 玩家可见说明

- 测试服已迁移至新的香港服务器，访问地址仍为 `https://test.grand-umi.com`。
- 测试服使用独立账号与对局数据，不会读写正式服玩家数据，后续功能改动可先在测试服验证。

## 技术说明

- 将测试服前端和 WebSocket 后端迁移到 `103.146.230.37`，使用独立的 systemd 服务、端口和 `/data/grandumi-test` 数据目录。
- 更新测试服部署脚本，支持新服务器首次初始化、目标提交脚本执行、失败回滚以及外网 HTTP 校验，并避免自动夹带未提交改动。
- 为 `test.grand-umi.com` 配置独立 Nginx 站点、Let's Encrypt 证书和自动续期，Cloudflare A 记录保持代理并切换至新服务器。
- 测试服卡图使用独立资源目录；部署时增量同步并审计原图、缩略图和展示图，缺失或过期时自动重新生成。

## 验证结果

- 测试服前后端构建成功，`grandumi-test-frontend.service` 与 `grandumi-test-backend.service` 均为 `active`。
- 外网首页最终返回 HTTP 200，`/backend/ready` 返回 `ready`，版本接口确认节点为 `hk-test-01`、提交为 `cd61086ebb1bf2f1b55870ac1ea95d18d842ed71`。
- 绕过 Cloudflare 直连 `103.146.230.37` 的 HTTPS 首页和健康接口验证通过。
- `wss://test.grand-umi.com/ws` WebSocket 握手成功并正常关闭。
- 卡图完整性审计通过；正式服前后端服务在迁移和验证后仍保持 `active`。
