# 修复 DSL 卡牌效果发布缺失

- 日期：2026-08-15
- 分类：修复
- 影响范围：对局卡牌效果、服务端发布与启动校验
- 状态：已完成

## 玩家可见说明

- 修复服务器更新后大量卡牌效果无法正确发动的问题。
- 后续版本若卡牌效果资源不完整，服务器会拒绝启动，避免异常版本继续提供对局。

## 技术说明

- 将 `Effects/Definitions/**/*.json` 纳入服务端构建与发布产物，保留运行时要求的目录结构。
- 增加发布后的 DSL 资源完整性校验；发布包缺少定义时直接令发布失败。
- DSL 定义目录缺失、为空或未加载到任何卡效时改为启动失败，不再仅记录告警后继续运行。

## 验证结果

- `dotnet publish 服务端WebSocket/GrandUMIServer.csproj -c Release`：成功，发布产物包含 61 个 DSL JSON 定义文件。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj`：998 项通过。
- DSL 发布完整性专项测试：1 项通过。
