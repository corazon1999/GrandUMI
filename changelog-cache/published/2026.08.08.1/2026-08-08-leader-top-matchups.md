# Leader 榜增加榜前十对战统计

- 日期：2026-08-08
- 分类：新增
- 影响范围：主页 Leader 胜率榜、Leader 对战统计
- 状态：已完成

## 玩家可见说明

- 点击 Leader 榜中的任意 Leader，即可查看它对阵当前排行榜前十名的胜率、优劣关系以及先攻和后攻表现。
- 对战结果使用“大优、优、平、小劣、劣”五档提示；低于 5 场的结果会标记样本不足，镜像对局单独展示。

## 技术说明

- 新增按时间范围和目标 Leader 聚合的榜前十对战统计协议，沿用排行榜的真人对局与最短回合过滤规则。
- 对战统计在点击榜单项时按需请求，并按周期与 Leader 缓存，避免扩大主榜单回包。

## 验证结果

- `dotnet test ".\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj" --filter "FullyQualifiedName~LeaderStatsStoreTests"`：通过 5 项测试。
- `npm run build -- --webpack`：Next.js 生产构建、TypeScript 检查和静态页面生成全部通过。
- `git diff --check`：通过。
