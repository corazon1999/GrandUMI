# 结算面板继续聊天

- 日期：2026-08-08
- 分类：优化
- 影响范围：对局结算、局内聊天、观战聊天
- 状态：已完成

## 玩家可见说明

- 对局进入结算面板后，双方玩家和观战者仍可使用左下角的局内聊天；返回大厅后会自动退出本场聊天。

## 技术说明

- 将局内聊天控件提升到结算遮罩之上，并新增离开赛后聊天协议。
- 权威房间结算后仅保留轻量会话路由，不保留游戏引擎或牌局状态；返回大厅、加入新对局、断线或超过 30 分钟时自动解绑。

## 验证结果

- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --filter FullyQualifiedName~PostGameChatRegistryTests --no-restore`：4 项通过。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --no-restore`：589 项通过。
- `npm run build`（`opcgpro-web`）：Next.js 生产构建与 TypeScript 检查通过。
