# 候选环境与正式服完全隔离

- 日期：2026-08-15
- 分类：优化
- 影响范围：香港候选环境、正式服发布流程、玩家数据安全
- 状态：已完成

## 玩家可见说明

- 测试与候选环境改为独立端口、独立数据和受控资源上限，后续版本验证不会再与正式对局争用服务入口或玩家数据。

## 技术说明

- 候选后端和前端分别迁移到 `18080` 与 `13000`，避免与正式服 `8080` 与 `3000` 冲突。
- 候选数据库和在线备份迁移到 `/data/grandumi-candidate`，不再读取或写入 `/data/grandumi`。
- 正式域名 Nginx 配置只承载 `grand-umi.com`，`candidate.grand-umi.com` 由独立站点和独立证书承载。
- 候选服务降低连接、房间、CPU 和内存上限，并继续复用正式服只读卡图资源。
- 正式客户端的 WebSocket 端点不再包含候选环境，避免故障切换到测试数据。

## 验证结果

- `node --test .\opcgpro-web\tests\ranked-match-clock.test.mjs .\opcgpro-web\tests\new-production-deploy.test.mjs`：15 项通过。
- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --filter FullyQualifiedName~RankedStoreTests --no-restore -p:UseSharedCompilation=false`：27 项通过。
- `npm run build`：Next.js 生产构建与 TypeScript 检查通过。
