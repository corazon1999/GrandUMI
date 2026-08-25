# 修复 OP08-032 反击值

- 日期：2026-08-26
- 分类：修复
- 影响范围：OP08-032 卡牌数据、组卡与对局反击值读取
- 状态：已完成

## 玩家可见说明

- OP08-032 的反击值现已修正为 2000；同批反馈中的 OP08-022 与 OP08-034 维持原有数值不变。

## 技术说明

- 仅修改明确回执所指向的 OP08-032，并重新生成前端卡牌合集与数据版本，避免把相同回执误套用到相邻卡号。

## 验证结果

- 精确回归先复现 OP08-032 反击值为 0 的失败，再同时断言 OP08-032 为 2000、OP08-022 与 OP08-034 为 0。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --no-restore --filter "FullyQualifiedName~G654_G655_G656|FullyQualifiedName~G738_OP06_092|FullyQualifiedName~G751_OP09_013"`：3 项通过。
- `npm run build:cards`：成功生成 62 个卡集、2823 张卡，数据版本 `82933ecf2b86`。
