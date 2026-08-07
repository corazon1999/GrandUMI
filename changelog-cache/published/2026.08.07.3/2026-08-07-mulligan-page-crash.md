# 调度选择页面崩溃修复

- 日期：2026-08-07
- 分类：修复
- 影响范围：对局开局、调度手牌交互
- 状态：已完成

## 玩家可见说明

- 修复开局调度阶段点击“更换”或“保留”后偶发整页加载失败的问题，选择完成后会正常进入对局。
- 调度选择请求处理期间会锁定操作按钮，避免网络延迟或连续点击造成重复提交。

## 技术说明

- 将调度倒计时副作用移到所有条件返回之前，确保调度状态切换前后的 React Hook 调用顺序保持一致。
- 使用全局请求处理中状态禁用按钮，并在发送入口再次校验，拦截同一调度选择的重复请求。

## 验证结果

- `npm.cmd run build`：Next.js 生产构建及 TypeScript 检查通过。
- 本地浏览器单人对局回归：“保留”和“更换”两条路径均正常关闭调度界面并进入主要阶段，页面保持在 `/game`，控制台无错误。
- `dotnet test D:\Self\GrandUMI\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj -c Release --nologo --filter "FullyQualifiedName~StartingPlayerFlowTests"`：9 项测试全部通过，覆盖双方调度完成与超时自动保留。
