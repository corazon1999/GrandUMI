# 修复 ST33-004 弃牌减费

- 日期：2026-08-11
- 分类：修复
- 影响范围：ST33-004「波尔萨利诺」手牌费用计算
- 状态：已完成

## 玩家可见说明

- 修复 ST33-004「波尔萨利诺」在我方为发动卡牌效果而丢弃手牌后，没有获得本回合费用 -3 的问题。

## 技术说明

- 效果结算期间的手牌丢弃现在会统一记录 ST33-004 所需的回合状态，包含效果文本冒号前支付的弃牌成本。
- 弃牌事件仍保留“是否为成本”的信息，其他卡牌效果可以继续按各自规则区分成本与非成本弃牌。
- 效果结算之外的反击等规则处理不会触发该减费。

## 验证结果

- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --filter "FullyQualifiedName~ST31To35EffectTests"`：通过 12 项，失败 0 项。
- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj`：通过 831 项，失败 0 项。
