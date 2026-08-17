# OP12-041 启动主要结算顺序修复

- 日期：2026-08-17
- 分类：修复
- 影响范围：对局卡牌效果 / OP12-041「山智」
- 状态：已完成

## 玩家可见说明

- OP12-041「山智」的「启动·主要」现在会先支付咚!!-1，再选择是否发动合格的《草帽一伙》事件。
- 只有【反击】而没有【主要】效果的事件不再出现于可发动列表。

## 技术说明

- 调整 OP12-041 启动效果的结算次序：支付咚!!-1 成本并记录每回合一次后，才筛选与选择手牌事件。
- 候选事件新增 `EventMain` 触发标签校验，防止纯反击事件被免费发动。

## 验证结果

- `QQCardEffectRegressionTests.OP12_041_ActivatedMain_ReturnsDonBeforeChoosingMainEventAndExcludesCounterOnlyEvents` 通过。
- 相关卡效专项测试通过。
