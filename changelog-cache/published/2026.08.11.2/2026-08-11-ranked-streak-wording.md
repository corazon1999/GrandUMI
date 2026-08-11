# 排位连胜公告文案优化

- 日期：2026-08-11
- 分类：优化
- 影响范围：排位赛结算、全服滚动公告
- 状态：已完成

## 玩家可见说明

- 排位连胜公告现在会展示获胜玩家打败的对手阵营与具体段位，并以更有对战氛围的文案播报当前连胜次数。

## 技术说明

- 排位赛结算后将败方的阵营标识与赛后段位传入公告格式化逻辑，统一转换阵营中文名和连胜中文数字；公告触发门槛及连续获胜后的后续播报规则保持不变。

## 验证结果

- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --filter FullyQualifiedName~GlobalAnnouncementPolicyTests`：3 项测试全部通过，服务端编译成功。
