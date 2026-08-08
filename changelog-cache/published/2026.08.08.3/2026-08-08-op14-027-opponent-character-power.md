# 修正 OP14-027 力量降低范围

- 日期：2026-08-08
- 分类：修复
- 影响范围：OP14-027「杰克斯」卡牌效果
- 状态：已完成

## 玩家可见说明

- 修正 OP14-027「杰克斯」在对方回合中的力量降低效果，现在只会使对方角色力量 -1000，不再影响双方 Leader 或己方角色。

## 技术说明

- 在 OP14-027 的持续效果判定中显式限定对方阵营及角色区域，避免持续效果作用域元数据未参与力量计算时扩大生效范围。

## 验证结果

- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --no-restore --filter "FullyQualifiedName~OP08_OP14RegressionTests"`：10/10 通过。
- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --no-restore`：661/661 通过。
