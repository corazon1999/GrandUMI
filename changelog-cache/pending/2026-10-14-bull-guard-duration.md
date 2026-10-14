# 蛮牛突进加攻持续到对方结束

- 日期：2026-10-14
- 分类：修复
- 影响范围：卡牌规则与结算（F01）
- 状态：已完成

## 玩家可见说明

持续到下个对方结束阶段结束。

## 修复前后区别

- 修复前：加攻在我方回合结束即消失。
- 修复后：持续到下个对方结束阶段结束。

## 技术说明

详见 `docs/feedback-triage-2026-10-14.md` 与对应实现差异；原始收件状态保持不变。

## 验证结果

第二批14项回归：修复前13失败、1通过；修复后14项全部通过。测试 `PlayerFeedback20261014SecondBatchTests` 覆盖该场景；失败、通过日志保存于 `E:\GrandUMI-Temp\FeedbackFix-20261014\before-second-batch.log` 与 `after-second-batch.log`。
