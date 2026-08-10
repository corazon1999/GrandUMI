# DON 放回选择明确显示附着目标

- 日期：2026-08-10
- 分类：修复
- 影响范围：对局内“咚!!放回咚!!卡组”选择界面
- 状态：已完成

## 玩家可见说明

- 选择要放回的咚!!时，附着中的咚!!会显示目标卡图、领袖或角色位置以及完整名称；即使多个目标名称相近或卡牌相同，也能看出每张咚!!贴在哪个目标上。

## 技术说明

- 服务端统一为 DON 选择项补充附着目标实例 ID、卡号和卡名。
- 客户端根据目标实例 ID 区分领袖与场上角色序号，并使用目标卡图和不截断名称呈现附着关系。

## 验证结果

- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --no-restore --filter "FullyQualifiedName~OP17_058_DonMinusPrompt_IncludesActiveRestAndAttachedDon"`：通过 1/1。
- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~OP17EffectTests"`：通过 46/46。
- `npm run build`（`opcgpro-web`）：Next.js 生产构建与 TypeScript 检查通过。
