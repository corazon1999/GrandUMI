# 宙斯效果 KO 可触发拉布离场保护

- 日期：2026-08-07
- 分类：修复
- 影响范围：OP11-106 宙斯、OP15-035 拉布及效果 KO 的离场替代结算
- 状态：已完成

## 玩家可见说明

- 修复 OP11-106 宙斯通过登场时效果 KO 角色时，OP15-035 拉布无法发动离场保护的问题。

## 技术说明

- 将 OP11-106 的旧同步 KO 调用切换为异步效果 KO 流程，正确携带效果发起方并派发防 KO、离场替代及【KO时】结算。
- 增加正式服反馈场景的回归测试，覆盖宙斯支付生命成本、拉布横置两张卡牌并保护目标角色的完整流程。

## 验证结果

- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --filter OP11_106`：通过 1，失败 0。
- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj`：通过 533，失败 0。
