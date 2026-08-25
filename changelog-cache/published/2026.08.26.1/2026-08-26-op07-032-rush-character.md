# 补齐 OP07-032 速攻角色能力

- 日期：2026-08-26
- 分类：修复
- 影响范围：OP07-032、登场回合攻击角色、前后端卡牌数据包
- 状态：已完成

## 玩家可见说明

- OP07-032 在登场回合现在可以正常攻击对方角色，但仍不能在登场回合攻击对方领袖。

## 技术说明

- 为 OP07-032 补齐结构化的【速攻：角色】能力，复用统一攻击校验对该正式词条与内部语义名的兼容处理。
- 同步根卡牌数据与前端卡牌数据，并重新生成聚合卡牌包及内容版本，确保服务端判定、客户端显示和缓存版本一致。

## 验证结果

- 精确回归先复现登场回合无法攻击角色，再验证同回合攻击休息角色合法、攻击领袖仍被拒绝。
- `npm run build:cards`：成功生成 62 个卡集、2,823 张卡的聚合数据包，版本 `be92d1b250d6`。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~G607_OP14_104|FullyQualifiedName~G616_OP06_111|FullyQualifiedName~G628_OP07_032" --verbosity minimal`：3 项通过。
