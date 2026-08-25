# 回合加时与挂机提醒

- 日期：2026-08-25
- 分类：优化
- 影响范围：排位、休闲公开匹配、对局棋钟、断线恢复
- 状态：已完成

## 玩家可见说明

- 公开匹配的每回合操作时间调整为 6 分钟；每位玩家每局可使用一次加时，为当前回合增加 2 分钟，最高不超过 8 分钟。
- 连续 1 分钟没有操作时会弹出服务端权威倒计时，此时距离自动判负还剩 3 分钟；同一段连续无操作达到 4 分钟将自动判负。贴咚、撤回贴咚和弹窗确认均会被识别为玩家操作，并把本次无操作计时归零。

## 技术说明

- 回合棋钟、挂机提醒和挂机判负共用房间串行队列、单调时钟、互斥锁及唯一计时任务；服务端排队、效果结算、断线宽限和平局协商期间不会继续向玩家扣时。
- 每局一次的加时使用状态写入房间恢复日志；恢复逻辑会忽略旧日志中已经废弃的累计挂机字段。连续无操作只在当前在线决策段内计算，服务重启及双方重新连接后从新的连续操作段开始。
- 客户端使用服务端 UTC 锚点与本机单调流逝显示倒计时；桌面棋钟与手机安全区分别提供加时入口，观战和回放不挂载玩家控制组件。

## 验证结果

- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --no-restore`：1276 项测试全部通过。
- `node --test tests/inactivity-turn-clock.test.mjs tests/ranked-match-clock.test.mjs tests/suggestion-list-ui.test.mjs tests/feedback-ui-actions.test.mjs tests/server-countdown-clock-skew.test.mjs tests/deck-editor-back-navigation.test.mjs`：27 项测试全部通过。
- `npx tsc --noEmit`：通过。
- `npm run build`：Next.js 生产构建通过。
- 浏览器双尺寸实机验证边界：本地页面与旋转布局壳可打开，但内置浏览器未完成本地 WebSocket 真局和受控状态注入，因此未把 390×844、360×780 的新控件视觉检查记录为通过；需在测试服真局补测。
