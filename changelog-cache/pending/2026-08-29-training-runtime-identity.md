# 训练重放运行身份与动作日志加固

- 日期：2026-08-29
- 分类：优化
- 影响范围：服务端对局日志、确定性重放准备层、AI 真人对练数据基础
- 状态：已完成

## 玩家可见说明

- 对局日志现在会精确记录运行版本以及真正被接受动作的参数和来源，为后续 AI 真人对练数据校验提供更可靠的基础，不改变现有对局规则。

## 技术说明

- 启动时一次性缓存完整 Git 对象 ID、当前核心程序集、卡表内容、规则集清单及确定性协议身份，新对局通过唯一工厂写入精确 `match_start`，避免逐局磁盘遍历。
- 新增 `grandumi.matchlog.v1.accepted-self-contained.v2`：accepted 必须自包含规范 `data`、`requestId`与 `source`，并与 requested 做唯一相关性及数据一致性审计；v2 不会退回 legacy 配对规则。
- 系统／机器人／超时动作显式标记为 `source=system`，可用于重放推进，但不会被当成真人训练标签；旧 adapter 仍保持兼容。
- 生产重放工件注册表仍为空，本次未冻结生产 checkpoint provider，因此仍不得将现有日志宣称为可训练数据集。

## 验证结果

- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --nologo --filter "FullyQualifiedName~TrainingReplayPreparationTests|FullyQualifiedName~ArtifactReplayWorkerTests|FullyQualifiedName~MatchLogTests|FullyQualifiedName~ReplayRuntimeIdentityTests"`：65/65 通过。
- 按 `ops/windows/GrandUmiTemp.ps1` 将测试临时根目录固定到 E 盘后执行服务端全量回归：1597/1597 通过，0 失败，0 跳过。
- 构建结果：0 警告、0 错误；`git diff --check` 通过。
