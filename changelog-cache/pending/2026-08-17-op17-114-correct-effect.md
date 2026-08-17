# OP17-114「甜点3将星」效果校正

- 日期：2026-08-17
- 分类：修复
- 影响范围：OP17-114「甜点3将星」登场效果
- 状态：已完成

## 玩家可见说明

- 「甜点3将星」在我方回合登场时，现在可以横置2张咚!!发动：抽1张牌，将卡组顶最多1张牌加入生命区，然后使对方最多2张角色本回合力量-3000。
- 发动前会先要求玩家确认；取消时不会横置咚或结算后续效果。
- 移除了错误的领航特征限制，任意领航均可按卡牌原文发动效果。

## 技术说明

- 恢复 OP17-114 登场效果的2咚费用、抽牌、可选生命追加及最多2个减力目标。
- 补充非《大妈海盗团》领航正常发动以及支付费用前取消的回归覆盖。

## 验证结果

- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --filter FullyQualifiedName~OP17_114 --no-restore`：2/2 通过。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --filter FullyQualifiedName~OP17EffectTests --no-build`：54/54 通过。
- 全量服务端测试 1096 项中 1095 项通过；现有 `SessionReplacementTests` 的中文提示文本断言失败，与本次卡牌效果改动无关。
