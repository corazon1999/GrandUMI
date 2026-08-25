# 对局记录补充开局结果概要

- 日期：2026-08-26
- 分类：新增
- 影响范围：对局记录、本地回放概要
- 状态：已完成

## 玩家可见说明

- 新完成的对局会在对局记录中显示我方骰子胜负，以及本局是先手还是后手，复盘前即可快速确认开局情况。
- 旧回放缺少这些信息时不会显示猜测结果，避免造成误导。

## 技术说明

- 本地终局元信息新增可选的骰子胜负与先后手字段，仅在先后手已经由权威快照确定后写入。
- 历史卡片使用可换行的紧凑徽标展示开局结果，保留旧 IndexedDB 记录的向后兼容。

## 验证结果

- `node --disable-warning=MODULE_TYPELESS_PACKAGE_JSON --test tests/match-history-opening-summary.test.mjs tests/disconnect-loss-history.test.mjs tests/draw-agreement-ui.test.mjs`：11 项通过。
- `npx tsc --noEmit --incremental false`：通过。
- 浏览器验证 `390×844` 与 `360×780` 两档手机竖屏：概要徽标完整可见、页面无横向溢出，删除按钮触控区保持 `44×44px`。
