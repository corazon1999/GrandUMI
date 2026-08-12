# 管理员维护排空模式

- 日期：2026-08-12
- 分类：新增
- 影响范围：管理员主面板、排位与休闲匹配、好友邀请、房间码、友谊战、单人对局
- 状态：已完成

## 玩家可见说明

- 管理员现在可以先启动维护，立即暂停所有新对局；正在进行的对局仍可自然结束，玩家会看到“维护更新中”的明确提示。
- 管理面板会实时显示剩余对局房间数，归零后提示可以开始正式服更新发布。

## 技术说明

- 服务端以管理员账号权限校验维护操作，并在最终建局入口使用原子准入门禁，覆盖排位、休闲、好友邀请、房间码、友谊战和单人模式，防止旧客户端或并发请求绕过。
- 启用维护时会取消匹配队列、待处理邀请和尚未开局的赛前房间，不中断权威房间池内已进行的对局；房间清理后向在线会话广播最新计数。
- 维护状态持久化到服务端数据目录，服务进程因发布重启后仍保持维护，直到管理员主动结束维护。

## 验证结果

- `dotnet build 服务端WebSocket/GrandUMIServer.csproj --nologo`：通过，0 警告、0 错误。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --filter FullyQualifiedName~GameMaintenanceStateTests`：3 项通过。
- `node --test opcgpro-web/tests/maintenance-mode.test.mjs opcgpro-web/tests/global-announcement.test.mjs opcgpro-web/tests/home-sidebar.test.mjs opcgpro-web/tests/lobby-platform-notice.test.mjs`：13 项通过。
- `npm run build`：通过，Next.js 生产构建及 TypeScript 检查成功。
- 浏览器实测桌面、390×844、360×780：维护控制条和玩家提示完整可见，主要按钮为 44px 高；修正手机端常驻设置按钮重叠后复查通过，无横向溢出。
- 仓库前端全量 151 项中 149 项通过；2 项失败源于并发新增的无关测试路径写错。后端全量 880 项中 879 项通过；无关的 EB03-008 卡效测试单独复跑仍失败。维护专项验证全部通过。
