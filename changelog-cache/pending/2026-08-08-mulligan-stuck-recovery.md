# 调度超时卡死恢复与投降入口

- 日期：2026-08-08
- 分类：修复
- 影响范围：网络对战开局调度、断线重连与局内菜单
- 状态：已完成

## 玩家可见说明

- 修复调度倒计时结束后仍可能一直停在等待界面的问题；客户端会主动同步服务端结果，异常时也可以重新同步或通过右上角菜单投降退出。

## 技术说明

- 调度倒计时归零后，客户端会有限次数请求权威状态，服务端据此补做超时玩家的自动保留并返回最新快照。
- 服务端将取状态和账号重绑恢复改为不可静默丢弃的房间任务，并为调度计时任务增加异常记录与有限重挂。
- 游戏菜单层级提升到开局遮罩之上，且不再因普通动作等待状态禁用，保证服务端允许的投降操作始终可达。

## 验证结果

- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --filter "FullyQualifiedName~MulliganRecoveryTests|FullyQualifiedName~StartingPlayerFlowTests"`：通过 12 项，失败 0 项。
- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --no-restore`：完整服务端测试通过 671 项，失败 0 项。
- `npm run build`：Next.js 16.2.6 生产构建及 TypeScript 检查通过。
- `git diff --check`：通过。
