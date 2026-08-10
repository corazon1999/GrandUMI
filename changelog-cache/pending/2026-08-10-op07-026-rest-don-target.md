# OP07-026 可选择休息咚

- 日期：2026-08-10
- 分类：修复
- 影响范围：对战卡牌效果、OP07-026 杰丽·邦妮
- 状态：已完成

## 玩家可见说明

- 修复 OP07-026「杰丽·邦妮」登场时无法选择对方休息状态咚!!的问题；现在可以选择休息角色或咚!!，使其在对方下个重置阶段不转为活跃。

## 技术说明

- 将对方休息角色与休息咚合并为同一目标选择，并通过咚候选数据在客户端显示单张咚。
- 选中咚后设置一次性的下次重置禁止激活标记，由重置阶段消费并清除。

## 验证结果

- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --filter FullyQualifiedName~OP07_026`：通过 2 项测试。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj`：通过 731 项测试。
