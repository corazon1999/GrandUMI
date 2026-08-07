# 修复 OP11-097 废弃区角色回收

- 日期：2026-08-08
- 分类：修复
- 影响范围：OP11-097「我这是彻底退步了啊……!!!」的反击效果
- 状态：已完成

## 玩家可见说明

- 使用「我这是彻底退步了啊……!!!」后，我方废弃区达到 10 张牌时，现在可以正常选择并回收费用不高于 3 的黑色角色卡牌。

## 技术说明

- 将 OP11-097 的回收候选颜色条件由错误的紫色修正为黑色。
- 保留原有的角色类型、原始费用不高于 3、废弃区至少 10 张和最多回收 1 张等条件。

## 验证结果

- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --filter OP11_097 --no-restore`：3/3 通过。
- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --no-restore`：544/544 通过。
