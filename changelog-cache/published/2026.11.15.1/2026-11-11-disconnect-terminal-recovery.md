# 掉线超时终局恢复修复

- 日期：2026-11-11
- 分类：修复
- 影响范围：公开匹配对局、断线宽限、终局清理与服务恢复
- 状态：已完成

## 玩家可见说明

- 玩家掉线宽限时间用尽后，对局会正确判定结束并释放账号；重新登录或开始下一局时，不会再被错误提示仍在另一场对局中。

## 技术说明

- 自动掉线超时和在线玩家主动结束断线对局统一使用服务端终局提交流程，先写入 `disconnect_timeout` 终局 WAL，再广播终局并执行清理。
- 终局清理失败或服务重启时，恢复逻辑可以识别已提交的掉线终局，不会将内存终局丢失后重放成进行中对局。

## 验证结果

- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --filter FullyQualifiedName~InactivityTerminalRecoveryTests`：9/9 通过。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj`：3636 通过、2 跳过、0 失败。
