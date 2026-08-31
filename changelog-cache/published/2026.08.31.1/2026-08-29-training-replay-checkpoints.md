# 测试服对局重放稳定点审计

- 日期：2026-08-29
- 分类：优化
- 影响范围：服务端对局日志、确定性训练重放、测试服配置
- 状态：已完成

## 玩家可见说明

- 优化测试服对局的一致性审计能力，为后续安全重建对局和训练数据质量检查补齐稳定点记录；不改变玩家操作规则或正式服行为。

## 技术说明

- 冻结当前工件的完整状态、公开状态和累计随机轨迹 checkpoint provider；完整摘要包含可能跨 Prompt 稳定点存在的 KO 原因、发起方与来源卡上下文。线上日志只持久化摘要、随机事件数量及 accepted 动作序号／稳定哈希绑定，不写入原始隐藏状态或身份字段。
- 在房间单读者边界内按“开局、每条 accepted／系统动作结算稳定后、终局且 match_end 之前”写入 checkpoint，并统一在线 accepted 与离线动作磁带的规范哈希算法；checkpoint 不因队列容量饱和丢弃，但后台磁盘写入失败仍由缺失契约整局 fail closed。
- checkpoint 功能代码默认关闭，仅在测试服 service 配置启用；进程恢复无法恢复累计随机轨迹时写入明确停用标记，准备层整局 fail closed。
- 终局摘要使用身份无关的原因类别，避免展示名进入状态摘要；未知原因类别仍由 terminal verifier 退回原文精确核对。
- 日志序号分配与 Channel 入队使用同一临界区，避免并发追加产生权威序号与物理 JSONL 顺序倒置。

## 验证结果

- `dotnet build 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --no-restore`：通过，0 警告、0 错误。
- 新增生产 checkpoint 定向测试 6 项全部通过，覆盖真实 JSONL→Prepare→生产 Provider Worker 双次往返、系统／真人动作、投降终局、Prompt 稳定态下 KO 上下文逐字段摘要 canary、隐私、随机轨迹顺序、恢复 fail closed、64 路并发日志顺序和性能门。
- 按 `ops/windows/GrandUmiTemp.ps1` 使用 E 盘测试目录运行服务端完整测试：1603 项全部通过，0 失败、0 跳过。
