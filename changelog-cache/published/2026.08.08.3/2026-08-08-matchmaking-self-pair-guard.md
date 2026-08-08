# 防止公开匹配到自身

- 日期：2026-08-08
- 分类：修复
- 影响范围：公开匹配、WebSocket 会话与对局房间创建
- 状态：已完成

## 玩家可见说明

- 修复连续点击匹配、网络消息重发或同账号旧连接残留时，偶发匹配到自己并卡在先后手选择界面的问题。
- 正常匹配会继续保留玩家的等待位置，直到找到另一名不同账号的有效对手。

## 技术说明

- 匹配队列改为原子取出并占用双方，跳过相同会话、相同账号和已被新登录替代的旧连接，同时清理重复队列项。
- 对局房间创建入口增加最终身份约束，禁止相同会话占据两个座位，并禁止相同账号作为真人对局双方。

## 验证结果

- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --nologo --filter FullyQualifiedName~MatchmakingIdentityTests`：4 项全部通过，0 失败、0 跳过。
- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --nologo`：675 项全部通过，0 失败、0 跳过。
- `git diff --check`：通过，无空白错误。
