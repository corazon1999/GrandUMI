# ST15-002 当前力量 KO 判定修复

- 日期：2026-08-10
- 分类：修复
- 影响范围：对战卡牌效果、ST15-002 爱德华·纽哥特
- 状态：已完成

## 玩家可见说明

- 修复 ST15-002 爱德华·纽哥特无法 KO 原力量较高、但已被降低至当前力量 5000 或以下角色的问题。

## 技术说明

- 将 ST15-002【启动主要】的目标过滤条件由原本力量不高于 5000 调整为当前力量不高于 5000，使判定包含临时减力、咚赋予及持续力量效果。
- 新增回归测试，覆盖原力量 12000 的角色被降低至当前力量 5000 后可被选中并 KO 的场景。

## 验证结果

- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --filter FullyQualifiedName~ST15EffectTests`：通过 1，失败 0。
- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj`：通过 722，失败 0，跳过 0。
