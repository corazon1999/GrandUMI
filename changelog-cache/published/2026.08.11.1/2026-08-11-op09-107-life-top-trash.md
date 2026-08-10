# OP09-107 妮古·罗宾登场效果修复

- 日期：2026-08-11
- 分类：修复
- 影响范围：OP09-107 妮古·罗宾的登场效果
- 状态：已完成

## 玩家可见说明

- 对方生命卡为 3 张或更多时，OP09-107 登场会正确将对方生命区最上方的 1 张卡牌放置到废弃区，不再查看或选择对方全部生命卡。

## 技术说明

- 将 OP09-107 的 DSL 结算由对方全部生命区的可选 KO，改为 `OppLifeToTrash`，以固定处理生命区顶牌且不触发 KO 结算。
- 新增登场时生命数达到 3 张与不足 3 张的回归测试，验证顶牌转移及门槛条件。

## 验证结果

- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --no-restore --filter FullyQualifiedName~OP09_107_RobinTests`：通过（2/2）。
- `OP09_wf.json` JSON 解析校验通过，`git diff --check` 无空白错误。
