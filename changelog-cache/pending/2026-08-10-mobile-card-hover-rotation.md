# 手机竖屏卡牌悬停详情旋转适配

- 日期：2026-08-10
- 分类：修复
- 影响范围：手机版竖屏自动横屏对局与回放、卡牌悬停详情
- 状态：已完成

## 玩家可见说明

- 手机竖屏进入对局或回放时，卡牌悬停详情现在会与牌桌一起旋转 90 度，并自动缩放、保持在屏幕可见区域内。

## 技术说明

- 在布局预览容器中下发四分之一圈旋转状态，使通过 Portal 挂载到页面根节点的悬停详情仍能感知牌桌方向。
- 按旋转后的视觉占位重新计算详情浮层位置，并在窄屏下等比缩放，避免旋转后超出视口。

## 验证结果

- `node --disable-warning=MODULE_TYPELESS_PACKAGE_JSON --test tests/card-hover-placement.test.mjs`：2 项通过。
- `node --disable-warning=MODULE_TYPELESS_PACKAGE_JSON --test tests/game-layout.test.mjs`：3 项通过。
- `tsc --noEmit`：通过。
- `next build`：代码编译成功；后续类型检查被本次任务范围外的 `src/components/ui/CardBack.tsx` 既有类型错误阻断。
