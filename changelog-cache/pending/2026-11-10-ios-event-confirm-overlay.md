# 修复 iOS 手机事件确认框右侧黑色遮挡

- 日期：2026-11-10
- 分类：修复
- 影响范围：对局中主要/反击事件的二次确认弹窗；iOS 手机竖屏自动旋转牌桌
- 状态：已完成

## 玩家可见说明

- iOS 手机竖屏对局中点击事件牌后，确认框不再在右侧生成黑色遮挡区域；取消和确认按钮可以完整显示并正常点击。

## 技术说明

- 将事件确认弹窗挂载到旋转对局的统一浮层门户，避免弹窗从右侧操作区内部的缩放牌桌继承 `fixed` 定位上下文，导致 iOS Safari 计算遮罩范围错误。
- 保留手机旋转布局的安全区适配和确认/取消按钮最小触控尺寸。

## 验证结果

- `node --test tests/counter-event-confirm.test.mjs tests/effect-confirm-prompt-layout.test.mjs tests/game-overlay-portals.test.mjs tests/game-layout.test.mjs`：18 项通过。
- 真实浏览器验证 390×844、360×780：主要/反击事件确认、取消、费用校验和生命费用边界通过；页面无浏览器异常，确认按钮完整可见且中心命中元素为按钮。
