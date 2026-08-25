# 修复 OP16-014 非 KO 离场替代

- 日期：2026-08-26
- 分类：修复
- 影响范围：OP16-014、对方效果置废弃区与置生命区、离场替代结算
- 状态：已完成

## 玩家可见说明

- OP16-014 因 OP09-009 的效果被放置到废弃区，或因 OP08-069 的效果被放入生命区时，现在会正常询问是否将自身 KO 来代替本次离场。
- 选择发动后会正常结算 OP16-014 的【KO 时】效果；选择不发动时，原本的置废弃区或置生命区效果会继续结算。

## 技术说明

- 将声明式卡效的非 KO 置废弃区操作与 OP08-069 的置生命区操作接入统一的对方效果离场守护入口，确保在移动卡牌前收集并结算 OP16-014 的可选替代效果。
- 保持离场置换的权威顺序：先确认是否发动并支付自身 KO 成本，再结算可选的【KO 时】复活；拒绝、取消或没有复活成本时均不会重复执行或错误跳过原离场。
- 同时补充 OP09-027 被 OP10-029 再活跃后同回合二次攻击不得重复抽卡的精确回归证据，用于关闭对应历史反馈。

## 验证结果

- 新增 6 项窄回归，覆盖 OP09-009 与 OP08-069 两条路径的确认发动、拒绝发动、无复活成本，以及 OP09-027 同回合再攻击限制。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~AllyLeaveGuardEffectTests|FullyQualifiedName~OncePerTurnEffectIndicatorTests.OP09_027_ReactivatedByOP10_029_DoesNotDrawOnSecondAttackInSameTurn"`：21 项通过。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --no-restore --nologo`：1,303 项服务端测试全部通过。
- `git diff --check`：本次 4 个代码与测试文件无空白错误。
