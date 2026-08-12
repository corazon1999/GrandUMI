# 手机对局全屏入口

- 日期：2026-08-12
- 分类：新增
- 影响范围：手机竖屏自动旋转后的对局与回放界面
- 状态：已完成

## 玩家可见说明

- 手机竖屏对局右下角新增全屏按钮，支持的浏览器可直接进入或退出全屏，减少地址栏和工具栏对牌桌空间的占用。
- iPhone Safari 无法直接进入网页全屏时，按钮会提示通过“添加到主屏幕”以全屏方式打开 GrandUMI。

## 技术说明

- 新增基于标准 Fullscreen API 与 WebKit 兼容接口的移动端全屏控件，并监听全屏状态同步切换图标与无障碍标签。
- 为 iOS 主屏幕 Web App 增加 Apple 元数据和 Web App Manifest；独立模式下自动隐藏不再需要的全屏按钮。
- 控件使用旋转画布安全区变量定位，并放入对局专用悬浮层，避免被路由子节点铺满规则拉伸。

## 验证结果

- `node --test tests/mobile-fullscreen-button.test.mjs` 通过。
- `node --disable-warning=MODULE_TYPELESS_PACKAGE_JSON --test tests/game-layout.test.mjs` 通过。
- `node --test tests/i18n.test.mjs` 通过。
- `next build` 通过。
- 浏览器实际检查 `390×844` 与 `360×780`：按钮与设置控件无重叠，触控区域分别为 `48×48px` 和约 `44.3×44.3px`；进入与退出全屏交互通过。
