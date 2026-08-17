# 修复 OP12-058 效果登场触发

- 日期：2026-08-17
- 分类：修复
- 影响范围：OP12-058 卡牌效果
- 玩家可见说明：OP12-058 从卡组登场的角色现在会正常发动其【登场时】效果，并依然获得本回合【速攻】。
- 技术说明：改为调用统一的卡组效果登场通道，自动入队后续 OnEnterField 结算。
- 验证结果：`UF024_OP12_058_TriggersOnPlayEffectOfCharacterPlayedFromDeck` 通过。
