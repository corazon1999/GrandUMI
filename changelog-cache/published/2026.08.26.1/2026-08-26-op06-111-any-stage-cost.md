# 修复 OP06-111 场地成本范围

- 日期：2026-08-26
- 分类：修复
- 影响范围：OP06-111 启动主要效果、双方场地归属与卡组底移动
- 状态：已完成

## 玩家可见说明

- OP06-111 发动效果时，现在可以选择双方场上任意一张费用为 1 的场地，并将其放回该场地持有者的卡组最下方。

## 技术说明

- 成本候选由仅我方场地扩展为双方场地，同时保留每个候选的权威持有者索引。
- 选择返回后重新核验场地仍在原持有者场地区且当前费用仍为 1，验证通过后才移动卡牌并消费每回合一次标记。

## 验证结果

- 精确回归先复现对方 1 费场地无法支付成本，再验证该场地只进入其持有者卡组底、不会进入发动方卡组，且后续休息角色效果正常结算。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~G607_OP14_104|FullyQualifiedName~G616_OP06_111|FullyQualifiedName~G628_OP07_032" --verbosity minimal`：3 项通过。
