# 菊之丞KO后的生命收益可选

- 日期：2026-10-14
- 分类：修复
- 影响范围：卡牌效果及对应交互（QQ #228）
- 状态：已完成

## 玩家可见说明

可以选择零张或一张；拒绝时不移动卡组卡牌。

## 修复前后区别

- 修复前：符合条件时强制把卡组顶一张加入生命。
- 修复后：可以选择零张或一张；拒绝时不移动卡组卡牌。

## 技术说明

DSL AddLifeFromDeck 支持显式 optional，保留对方生命不多于三张条件与触发登场实现。
来源对应和完整调查见 `docs/feedback-triage-2026-10-14.md`。

## 验证结果

`PlayerFeedback20261014Tests` 第一批13项回归：修复前11失败、2通过；修复后13项全部通过。
失败与通过日志分别为 `E:\GrandUMI-Temp\FeedbackFix-20261014\before-first-batch.log` 和 `after-first-batch.log`。
