# 修复 OP17-018 反击效果的力量条件

- 日期：2026-08-10
- 分类：修复
- 影响范围：OP17-018 事件卡反击效果
- 状态：已完成

## 玩家可见说明

- 使用 OP17-018 的反击效果时，角色通过咚或其他效果达到 8000 力量后，现在会正确计入发动条件；力量已降至 8000 以下的角色也不会被错误计入。

## 技术说明

- 将“我方有 2 张力量 8000 以上角色”的条件由卡面印刷力量改为引擎实时力量，纳入咚、回合修正、战斗修正和持续效果。
- 新增回归测试，覆盖低于 8000 的角色经加成达到条件，以及原本高力量角色被削减后不再满足条件。

## 验证结果

- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj -c Release --filter FullyQualifiedName~OP17_018`：通过（2/2）。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~OP17EffectTests`：通过（46/46）。
