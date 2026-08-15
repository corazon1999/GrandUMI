# 对局内使用展示名

- 日期：2026-08-15
- 分类：修复
- 影响范围：对局界面、观战、对局结算提示、断线恢复
- 状态：已完成

## 玩家可见说明

- 进入对局后，双方姓名和结算提示现在显示玩家设置的展示名，不再显示仅用于登录的账号名。

## 技术说明

- 对局状态同时保留内部登录账号与公开展示名，面向客户端的玩家快照只输出展示名。
- 投降、断线、操作时间耗尽、生命耗尽和卡组耗尽等公开结算原因统一使用展示名。
- 对局动作日志持久化展示名，服务重启恢复后继续保持；旧日志没有展示名时兼容回退账号名。

## 验证结果

- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --filter "FullyQualifiedName~GameDisplayNameTests" --no-restore`：通过 2 项专项测试。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --no-restore`：全部 991 项测试通过。
- 代码审计确认对局姓名区域继续使用既有截断布局，展示名与账号名同为最多 32 个字符，不改变桌面端和移动端控件尺寸。
