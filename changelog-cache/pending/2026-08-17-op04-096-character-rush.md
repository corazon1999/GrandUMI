# 补齐 OP04-096 斗牛竞技场速攻角色效果

- 日期：2026-08-17
- 分类：修复
- 影响范围：对局卡牌效果、《德莱斯罗兹》舞台与攻击交互
- 状态：已完成

## 玩家可见说明

- OP04-096 斗牛竞技场现在会在领袖拥有《德莱斯罗兹》特征时，为我方《德莱斯罗兹》角色赋予“速攻：角色”；符合条件的新登场角色可立即攻击对方角色，但不能攻击领袖。
- 牌桌会正确显示该角色获得的“速攻：角色”状态。

## 技术说明

- 斗牛竞技场的持续效果改为赋予正式关键词“速攻：角色”，攻击校验同时兼容正式词条与历史内部语义名。
- 服务端公开快照补充“速攻：角色”动态关键词，并将旧脚本的内部词条统一映射为客户端可识别的正式词条。

## 验证结果

- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --filter FullyQualifiedName~OP04_OP10EffectCompletionTests --no-restore`：6/6 通过，覆盖合法攻击角色、禁止攻击领袖、领袖/角色特征限制及快照关键词。
- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --no-restore`：1074/1074 通过。
- `node --test tests/standard-legal-subscript-one.test.mjs tests/leader-keyword-effects.test.mjs`：4/4 通过。
- `npm run build`：生产构建成功。
- 浏览器检查：桌面端及 `390×844`、`360×780` 竖屏均无横向溢出，现有主要按钮触控区域不小于 `44×44px`。
