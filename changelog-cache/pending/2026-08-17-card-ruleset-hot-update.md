# 卡牌效果分版本热更新

- 日期：2026-08-17
- 分类：新增
- 影响范围：服务端卡牌效果、对局恢复、管理员维护工具、对局结束提示
- 状态：已完成

## 玩家可见说明

- 卡牌效果修复现在可以只应用到之后开始的对局；正在进行的对局会按开局时的规则正常打完，不会在中途改变卡效。
- 使用旧规则的对局结束后，玩家会收到卡牌效果已更新、下一局开始生效的提示。

## 技术说明

- 新增不可变卡效规则集与运行时规则包加载器，支持 DSL 定义和 `IScriptedEffect` 插件按版本继承覆盖，并以原子指针切换新对局默认版本。
- 每局在创建时锁定 `rulesetId`，写入对局日志与快照；断线、服务重启和动作重放按原版本重新绑定，不会读取当前默认版本。
- 新增管理员规则包刷新/激活协议及主页控制面板，展示各版本进行中房间数；Prometheus、存活检查和版本接口同步暴露当前规则版本。
- 规则包采用唯一版本 ID，旧规则对象与插件加载上下文在进程内保留，确保旧对局自然结束前仍可调用原实现。

## 验证结果

- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --no-restore --filter "FullyQualifiedName!~SessionReplacementTests"`：1101 项通过。
- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --no-restore --filter FullyQualifiedName~CardRulesetHotUpdateTests`：2 项通过，覆盖同卡多版本并存、引擎锁定和重放恢复。
- `npx tsc --noEmit --incremental false`：通过。
- `node --test tests/ruleset-hot-update.test.mjs`：4 项通过，覆盖终局提示、激活协议、恢复链路与移动端管理面板约束。
