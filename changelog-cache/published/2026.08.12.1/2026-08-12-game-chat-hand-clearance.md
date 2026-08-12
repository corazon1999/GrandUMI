# 对局聊天与观战标识避让手牌

- 日期：2026-08-12
- 分类：修复
- 影响范围：手机竖屏自动旋转后的对局界面
- 状态：已完成

## 玩家可见说明

- 在手机竖屏进行对局时，聊天按钮和观战人数眼睛图标会显示在手牌下方，不再遮挡手牌。

## 技术说明

- 对局聊天控件会识别旋转画布，并在该布局中改用与实际屏幕下方对应的一侧定位；桌面和普通横屏布局保持原有位置。
- 两种定位均继续使用布局层安全区变量，兼容刘海和浏览器工具栏。

## 验证结果

- `node --test tests/spectator-controls.test.mjs` 通过。
- `node --disable-warning=MODULE_TYPELESS_PACKAGE_JSON --test tests/game-layout.test.mjs` 通过。
- `next build` 通过。
