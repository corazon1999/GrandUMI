# OP16-110 按当前费用判定横置目标

- 日期：2026-08-10
- 分类：修复
- 影响范围：OP16-110「巴斯克·乔特」的【KO时】效果与【触发】效果
- 状态：已完成

## 玩家可见说明

- OP16-110 现在会按角色的当前费用选择横置目标；获得费用增加效果而超过 6 费的角色不再能被该效果横置。

## 技术说明

- 为 OP16-110 的 KO 时与生命触发两条 DSL 选择路径增加当前费用不高于 6 的过滤，费用计算包含场上的持续费用增益。
- 新增 OP17-089 持续费用增益与 OP16-110 交互的回归测试，并保留当前费用恰好为 6 时可选的边界验证。

## 验证结果

- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --filter FullyQualifiedName~OP16_110_CurrentCostTests --no-restore`：通过 3 项。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --filter FullyQualifiedName~OP16SmokeTests --no-restore`：通过 20 项。
