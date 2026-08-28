# 确定性训练重放基础层

当前切片只负责把一份完整 JSONL 对局转换为“精确工件身份 + seed + 两副原始牌组 + accepted／系统动作磁带”，或给出整局隔离记录。

冻结边界：

- `Artifacts/replay-artifact-registry.v1.json` 初始不登记任何生产工件。旧日志缺少精确引擎、规则包、卡表、RNG、实例 ID、开局协议和重放配置身份，因此必须隔离，绝不回退到当前 `main`。
- `grandumi.matchlog.v1.accepted-pairing.v1` 只接纳能唯一配对的 requested → accepted 动作；rejected 不进入磁带。
- `starting_player_choice_timeout_auto_select` 需要后续 accepted 确认；`mulligan_timeout_auto_keep` 按当前权威实现直接映射为系统动作。系统动作只推进重放，不是真人训练标签。
- 旧 `prompt_timeout` 没有完整 `chosen`，当前版本整局隔离。后续只有在对应历史工件与 adapter 能精确恢复其语义时才可登记。
- 输出动作按日志应用序号确定性排序，动作、磁带、准备结果和隔离记录都使用规范 JSON 的 SHA-256。

尚未包含：artifact 进程 worker、实际引擎 checkpoint 重放、动作前 observation、P0-B 合法动作集合、批量断点/manifest 和数据集导出。这些门禁未完成前，不得把本层产物称为可训练数据集。
