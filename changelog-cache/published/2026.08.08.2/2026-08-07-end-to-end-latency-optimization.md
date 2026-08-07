# 对局端到端延迟优化

- 日期：2026-08-07
- 分类：优化
- 影响范围：对局操作响应、WebSocket 传输、观战、对局录像、延迟诊断
- 状态：已完成

## 玩家可见说明

- 优化对局操作与状态同步链路，网络正常时操作反馈更快，观战人数增加时也更不容易出现延迟堆积。
- 长对局录像改为边打边分块保存，减少浏览器内存持续增长导致的后期卡顿。
- 新增自动增量状态同步；发生丢包、重连或版本不兼容时会自动恢复完整状态，不影响旧客户端继续使用。

## 技术说明

- 服务端慢路径改为固定桶聚合统计，每分钟输出各阶段 P50/P95/P99、最大耗时、消息体积及队列深度；高频协议日志默认关闭，心跳不再争用全局控制台输出锁。
- 心跳增加请求编号回显，游戏动作与 Prompt 回包增加 requestId，客户端可准确统计 RTT 与动作到快照/拒绝回包的端到端耗时，反馈报告会附带网络诊断摘要。
- WebSocket 消息按对象身份缓存 UTF-8 序列化结果，同一观战快照可跨连接复用；回放与训练日志通过惰性共享 JSON 值复用公开快照物化结果。
- 新协议按连接最后实际发送的 Tick 生成顶层及玩家属性级增量；仅客户端声明能力后启用，每 32 次增量强制发送完整快照，增量节省不足 10% 时自动改发完整快照，基线错位时客户端自动请求 Resync。
- 浏览器本地录像数据库升级为 v2，每 16 帧异步写入一个 IndexedDB 分块，同时保留对旧版整块录像的读取兼容。

## 验证结果

- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --filter "FullyQualifiedName~LatencyOptimizationTests" -c Release --nologo`：通过 9 项，失败 0 项。
- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj -c Release --nologo`：通过 547 项，失败 0 项。
- `npm.cmd run build`：Next.js 生产构建与 TypeScript 检查通过，9 个页面全部成功生成。
- `git diff --check`：通过，无空白错误。
