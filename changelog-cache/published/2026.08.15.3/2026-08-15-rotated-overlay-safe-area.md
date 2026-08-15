# 旋转对局浮层安全区适配

- 日期：2026-08-15
- 分类：优化
- 影响范围：手机竖屏对局、卡牌详情、卡牌放大、生命区、废弃区、游戏菜单
- 状态：已完成

## 玩家可见说明

- 手机竖屏自动旋转对局画布时，卡牌详情、卡牌放大、生命区、废弃区和游戏菜单会按旋转后的安全区域正确显示并支持滚动。

## 技术说明

- 共享弹窗在旋转画布内改用容器查询宽高，并根据安全区变量限制宽度、高度与内边距。
- 旋转对局内不再使用未旋转视口的移动端底部抽屉布局，弹窗主体增加独立滚动区域。
- 生命区、废弃区和卡牌放大浮层改用容器单位，异画切换、关闭和游戏菜单按钮扩大到至少 48px。
- 游戏菜单固定位置改用项目布局安全区变量，避免与刘海、圆角和其他常驻控件重叠。
- 全局设置入口、语言、卡牌大小、动画速度、静音与试听操作统一扩大为至少 48px 触控区。

## 验证结果

- `node --test .\opcgpro-web\tests\deck-editor-mobile-card-preview.test.mjs .\opcgpro-web\tests\game-overlay-portals.test.mjs`：10 项通过。
- `node --test .\opcgpro-web\tests\player-feedback-prompt-ui.test.mjs .\opcgpro-web\tests\game-overlay-portals.test.mjs`：12 项通过。
- `npm run build`：Next.js 生产构建和 TypeScript 检查通过。
- 同批 UI 已在 `390×844` 与 `360×780` 浏览器视口下完成页面级无溢出检查。
