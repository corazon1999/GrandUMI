# OP14-037 原本力量筛选修复

- 日期：2026-08-17
- 分类：修复
- 影响范围：对局卡牌效果 / OP14-037「打发时间」
- 状态：已完成

## 玩家可见说明

- OP14-037「打发时间」现在只能 KO 原本力量不高于 7000 的对方休息角色，不会再将原本力量 8000 的角色列为目标。

## 技术说明

- 为 OP14-037 主要效果的休息角色选择步骤补充 `originalPowerLte: 7000` 过滤。
- 回归测试同时放入原本力量 7000 与 8000 的休息角色，确认只有前者进入候选。

## 验证结果

- `QQCardEffectRegressionTests.OP14_037_EventMain_OnlyOffersRestingCharactersWithOriginalPowerAtMost7000` 通过。
- 相关卡效专项测试通过。
