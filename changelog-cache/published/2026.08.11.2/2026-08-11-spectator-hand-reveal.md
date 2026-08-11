# 观战手牌授权后正确显示

- 日期：2026-08-11
- 分类：修复
- 影响范围：对局页观战、手牌隐私授权
- 状态：已完成

## 玩家可见说明

- 观战者申请查看主视角手牌并获玩家同意后，现在会立即看到该玩家当前及后续手牌；另一方手牌仍保持隐藏。

## 技术说明

- 对局牌桌接入观战快照中的个人手牌授权状态，并将“终局公开双方手牌”与“仅向获授权观战者公开主视角手牌”拆分为独立渲染条件，避免服务端已下发牌号但前端仍强制显示牌背。

## 验证结果

- `node --test tests/spectator-controls.test.mjs tests/game-over-hands.test.mjs`：6 项通过。
- `npm run build`：Next.js 生产构建与 TypeScript 检查通过。
- `dotnet test 服务端WebSocket.Tests --filter FullyQualifiedName~SpectatorPerspectiveTests --no-restore`：5 项通过。
- 浏览器以 `390×844`、`360×780` 两档手机竖屏检查本地页面，无横向或纵向溢出，可见按钮完整且触控尺寸不小于 `44×44px`。
