# 暂存贴咚立即同步力量

- 日期：2026-08-22
- 分类：修复
- 影响范围：对局页贴咚、领袖与角色力量显示
- 状态：已完成

## 玩家可见说明

- 在自己的回合给领袖或角色贴咚后，场上力量会立即按实际贴咚数量同步增加，无需等到攻击或执行下一项操作后才刷新。
- 连续贴咚和撤回贴咚时，力量与咚数量会保持一致，方便玩家及时判断场上局势。

## 技术说明

- 乐观贴咚除更新费用区与目标附着咚数量外，在持有者回合按每张咚 1000 力量同步更新领袖的 `leaderPower` 或角色的 `powerCurrent`。
- 沿用现有权威快照回滚与暂存队列重叠机制，撤回、动作被拒绝或收到中途快照时会重新得到一致的力量预览；服务端快照仍负责最终规则结算与条件性卡牌效果校正。

## 验证结果

- `node --test tests/optimistic-attach-don-power.test.mjs tests/suggestion-list-ui.test.mjs tests/feedback-ui-actions.test.mjs tests/game-layout.test.mjs`：16 项通过。
- `npm exec tsc -- --noEmit`：通过。
- `npm run build`：Next.js 生产构建通过。
- 本地浏览器在 `390×844`、`360×780` 两档竖屏下无横向溢出，主要按钮触控高度不小于 44px；对局竖屏旋转布局自动化回归通过。
