# OP12-099「卡尔加拉」对方生命离场触发修复

- 日期：2026-08-17
- 分类：修复
- 影响范围：OP12-099「卡尔加拉」我方回合中效果
- 状态：已完成

## 玩家可见说明

- 修复我方回合中对方生命卡牌离开生命区时，「卡尔加拉」未能发动抽1张牌效果的问题。
- 我方或对方任意一方的生命卡牌离开时，现在都会按卡牌原文发动；对方回合仍不会发动。

## 技术说明

- 移除 OP12-099 对生命离场事件持有者的错误己方限定，同时保留我方回合条件和无效事件保护。
- 使用实际移除对方生命的卡牌效果补充端到端回归测试，并覆盖对方回合不触发的边界。

## 验证结果

- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --filter FullyQualifiedName~OP12_099_KalgaraTests --no-restore`：2/2 通过。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --filter FullyQualifiedName~OP12 --no-build`：23/23 通过。
