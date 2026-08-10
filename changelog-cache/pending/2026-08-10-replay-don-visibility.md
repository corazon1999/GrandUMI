# 回放咚区域显示修复

- 日期：2026-08-10
- 分类：修复
- 影响范围：对局录像回放牌桌
- 状态：已完成

## 玩家可见说明

- 观看录像时，双方的咚!!费用区和咚!!卡组现在会正常显示，可完整复盘费用使用与贴咚情况。

## 技术说明

- 移除了回放模式对 `GameBoard` 中咚区域的条件隐藏；回放继续复用服务端快照中的双方费用区状态。
- 新增源码回归测试，防止回放分支再次移除咚区域。

## 验证结果

- `node --test tests/replay-don-visibility.test.mjs` 通过。
- `node --test tests/don-rest-orientation.test.mjs` 通过。
- `node scripts/build-card-bundle.mjs` 与 `next build` 通过。
