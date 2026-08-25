# 修复 OP09-013 领袖加成时效

- 日期：2026-08-26
- 分类：修复
- 影响范围：OP09-013 登场时效果、跨回合力量修正清理
- 状态：已完成

## 玩家可见说明

- OP09-013 登场时给予领袖的 +1000 力量现在会正确持续到下个对方回合结束，而不是在当前回合结束时提前消失。

## 技术说明

- 将本回合力量修正改为带施加方标识的“持续到下个对手结束阶段”修正，复用回合引擎现有的定向清理语义。

## 验证结果

- 精确回归先复现加成进入本回合字段的错误，再验证其不会在发动方结束阶段清除，并会在对手结束阶段清除。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --no-restore --filter "FullyQualifiedName~G654_G655_G656|FullyQualifiedName~G738_OP06_092|FullyQualifiedName~G751_OP09_013"`：3 项通过。
