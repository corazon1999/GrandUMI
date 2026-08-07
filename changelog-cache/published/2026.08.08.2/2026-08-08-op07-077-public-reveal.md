# 修复 OP07-077 检索卡牌未公开

- 日期：2026-08-08
- 分类：修复
- 影响范围：OP07-077「要去拿下『大秘宝』了！！！」的主要与触发效果
- 状态：已完成

## 玩家可见说明

- 「要去拿下『大秘宝』了！！！」将卡牌加入手牌时，现在会向双方正常公开被选中的卡牌，不再显示为非公开选择。

## 技术说明

- 在 OP07-077 完成检索并将选中卡牌加入手牌后，补发 `RevealCards` 公开消息。
- 公开内容仅包含实际加入手牌的卡牌；未选卡时不广播，也不会泄露其余查看到的卡牌。

## 验证结果

- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --filter OP07_077 --no-restore`：2/2 通过。
- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --no-restore`：549/549 通过。
