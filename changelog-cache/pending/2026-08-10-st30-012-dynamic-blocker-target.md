# ST30-012 可选择动态获得阻挡者的角色

- 日期：2026-08-10
- 分类：修复
- 影响范围：ST30-012 攻击时效果、动态关键词目标筛选
- 状态：已完成

## 玩家可见说明

- 修复 ST30-012 攻击时无法选择因生命条件而获得【阻挡者】的 OP13-031；对方仅剩 1 张生命时，该角色现在可被正确选择并转为休息状态。

## 技术说明

- 将 ST30-012 攻击时效果的目标过滤由卡面特征匹配改为当前实际关键词匹配，使筛选包含持续效果和临时效果授予的【阻挡者】。
- 新增 OP13-031 在对方 1 生命时动态获得【阻挡者】并被 ST30-012 选择的回归用例。

## 验证结果

- 在隔离 worktree 中运行 `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --filter FullyQualifiedName~ST30EffectTests --no-restore`，11 项测试全部通过。
- 在隔离 worktree 中运行 `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --no-restore`，690 项测试全部通过。
