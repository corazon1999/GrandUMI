# 调度超时与刷新恢复修复

- 日期：2026-08-08
- 分类：修复
- 影响范围：对局开局、调度手牌、断线与刷新恢复
- 状态：已完成

## 玩家可见说明

- 修复开局调度倒计时归零后仍一直等待对手、无法进入第一回合的问题；超时玩家会由服务器自动保留手牌。
- 对局页面刷新后会使用当前标签页保存的账号自动找回进行中的对局，不再需要返回首页手动重新进入。

## 技术说明

- 调度截止任务会反复核对服务端权威时间，并通过可靠房间队列写入等待结算，避免计时器提前唤醒或队列暂满导致唯一超时任务丢失。
- 请求状态和账号重绑在发送恢复快照前会补做过期调度结算，使异常丢失计时器的房间也能自愈。
- 对局页刷新时写入标签页级一次性恢复标记；新 WebSocket 握手完成后使用已保存账号登录，并由服务端按账号重新绑定原房间。

## 验证结果

- `dotnet test D:\Self\GrandUMI\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj -c Release --nologo --filter "FullyQualifiedName~StartingPlayerFlowTests|FullyQualifiedName~MulliganRecoveryTests"`：11 项测试全部通过。
- 新增回归覆盖调度计时可靠推进，以及刷新账号重绑前补做过期调度两条路径。
- `D:\Self\GrandUMI\opcgpro-web\node_modules\.bin\next.cmd build`：Next.js 生产构建及 TypeScript 检查通过。
