# 补全吞噬灵魂触发条件说明

- 日期：2026-09-03
- 分类：优化
- 影响范围：测试服管理员海克斯品质面板及对局内海克斯说明
- 状态：已完成

## 玩家可见说明

- “吞噬灵魂”现会明确显示其触发条件：己方效果使敌方角色离场，或将敌方活跃角色转为休息时，每回合首次会使己方领袖本回合力量+2000。

## 技术说明

- 仅更新海克斯目录中编号 42 的展示说明，并补充目录文案回归断言；不修改效果触发逻辑或运行时品质配置。

## 验证结果

- 已核对触发入口：仅在己方效果使敌方角色离场，或使敌方角色由活跃转为休息时处理，且每回合仅首次生效。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --filter FullyQualifiedName~HexModeStateMachineTests`：14/14 通过。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --no-restore --filter FullyQualifiedName~HexModeEffectsTests`：77/77 通过。
- `git diff --check`：通过。
