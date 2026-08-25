# 修复 OP08-069 复合成本的原子结算

- 日期：2026-08-26
- 分类：修复
- 影响范围：OP08-069、咚!!-1 与丢弃手牌复合成本、提示取消与重复响应
- 状态：已完成

## 玩家可见说明

- OP08-069 的登场时效果现在必须完整支付“咚!!-1，并丢弃 1 张手牌”后才会把卡组顶加入生命区。
- 在选择手牌时取消、超时或提交异常响应，不再出现已经退回咚!!却仍获得生命，或只支付一半成本的情况。

## 技术说明

- 新增复合成本原子操作：先按卡文顺序收集咚!!与手牌选择，待两步选择都完整且合法后，再重新核验权威区域与状态并一次性提交。
- 取消、空响应、重复 ID、非法 ID，以及等待期间发生恢复或状态变化时均保持零修改；完整验证之后的成本提交阶段不再跨越异步等待窗口。
- 重复提交相同提示 ID 仍由提示系统只接受首个有效响应，保证咚!!、手牌与生命区各只变化一次。

## 验证结果

- 新增 4 项精确回归，覆盖第二步取消后零修改、非法或重复咚!!响应、正常完整支付，以及真实提示系统中的重复响应幂等性。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~QqFeedback20260826RemainingRegressionTests|FullyQualifiedName~AtomicOpsTests|FullyQualifiedName~NewAtomicOpsTests|FullyQualifiedName~PromptContinuationTests|FullyQualifiedName~OptionalCostPromptReturnTests|FullyQualifiedName~AllyLeaveGuardEffectTests" --verbosity minimal`：43 项通过。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --no-restore --nologo --verbosity minimal`：1,323 项服务端测试全部通过。
