# 登录页补充学术项目介绍与免责声明

- 日期：2026-08-18
- 分类：优化
- 影响范围：首页登录面板
- 状态：已完成

## 玩家可见说明

- 登录页现在展示 TCG Intelligence Project 学术研究项目介绍，并在登录面板下方明确说明项目独立性、素材权利归属及平台不提供付费服务。

## 技术说明

- 替换登录卡片原有副标题，在卡片下方新增语义化免责声明区域。
- 免责声明采用响应式宽度、可换行文本和页面安全区布局，兼容桌面端与手机竖屏。
- 更新登录页专项回归测试，校验完整文案、展示顺序和移动端布局约束。

## 验证结果

- `node --test tests/unofficial-notice.test.mjs tests/login-password-memory.test.mjs tests/session-replaced-login.test.mjs tests/ws-endpoint-fallback.test.mjs`：13 项全部通过。
- 实际浏览器检查 `390×844`、`360×780`、`1920×1080`：项目介绍与免责声明完整可见，无横向溢出；主操作按钮保持 48px 高且未与免责声明重叠。
- `git diff --check`：通过。
- `npx tsc --noEmit`：被工作区中与本次任务无关的未提交 `src/app/feedback-ui-check/page.tsx` 3 处既有 `PlayerView` 类型错误阻断；本次修改未新增 TypeScript 错误。
