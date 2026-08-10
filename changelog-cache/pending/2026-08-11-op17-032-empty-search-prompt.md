# OP17-032 空检索仍显示确认窗口

- 日期：2026-08-11
- 分类：修复
- 影响范围：OP17-032，以及共用该检索流程的红发海盗团检索效果
- 状态：已完成

## 玩家可见说明

- 使用 OP17-032 检索时，即使牌堆顶没有符合条件的卡牌，也会正常显示已确认的牌堆顶卡牌，不再看起来像效果没有发动。

## 技术说明

- 调整 OP17 共用的牌堆顶检索流程：无匹配候选时仍创建包含全部已确认卡牌的 LookTop 提示，并仅允许从实际匹配候选中选择。
- 新增 OP17-032 在无符合条件卡牌时的回归测试，验证提示、可见卡牌和牌堆状态。

## 验证结果

- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --filter "FullyQualifiedName~OP17_SearchTop_IncludesSubgroupTrait_AndRevealsOnlyAddedCard|FullyQualifiedName~OP17_032_OnPlay_ShowsLookTopPrompt_WhenNoEligibleCardIsFound"`：通过（4/4）。
