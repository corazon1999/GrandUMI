# 确定性训练重放与隔离 worker 基础层

当前切片负责：

- 在进程启动阶段冻结完整运行身份：完整 Git 对象 ID、当前入口程序集 SHA-256、卡表内容清单、规则集清单、RNG／确定性 ID／开局协议和重放配置版本；
- 新对局只通过唯一工厂写入带精确身份的 `match_start`，逐局和逐动作路径不再扫描卡表、规则包或二进制文件；
- 把完整 JSONL 对局转换为“精确工件身份 + seed + 两副原始牌组 + accepted／系统动作磁带”；
- 通过可替换的 `IArtifactReplayWorker` 边界，把动作磁带接入精确登记工件对应的 `MatchReplay`；
- 在开局、每条 accepted／系统动作后的稳定点和终局核对显式 checkpoint 契约；
- 任一异常、拒绝、超时或 full/public/random/terminal 分歧都只返回整局隔离记录，不返回部分 checkpoint 或样本。

冻结边界：

- `Artifacts/replay-artifact-registry.v1.json` 不登记任何生产工件。旧日志缺少精确引擎、规则包、卡表、RNG、实例 ID、开局协议和重放配置身份，因此必须隔离，绝不回退到当前 `main`。
- dispatcher 按完整 descriptor 指纹绑定 worker；相同 `engineArtifactId` 但 binary/rules/cardDb/executable 等任一字段不同都会隔离。进程内实现只是可替换边界，未来独立进程代理使用同一可序列化 request/response contract。
- dispatcher 会复算 response 规范哈希，逐项核对成功结果的 source、prepared、tape、contract、registry、artifact、worker、request lineage；失败结果的稳定 reason/stage 和 source/action 定位也必须落在请求范围内。失败消息不进入稳定哈希。
- `grandumi.matchlog.v1.accepted-self-contained.v2` 强制每条 accepted 自包含规范 `data`、`requestId`和真实 `source`；requested 只用于相关性与篡改审计，不再作为训练动作数据的权威来源。
- `grandumi.matchlog.v1.accepted-pairing.v1` 仍只接纳能唯一配对的旧 requested → accepted 动作；两个 adapter 严格分流，v2 不会退回旧配对语义。rejected 始终不进入磁带。
- v2 的先后手和调度超时动作也必须由后续自包含 accepted 确认；只有 legacy adapter 保留旧 `mulligan_timeout_auto_keep` 的直接映射。`source=system` 只推进重放，绝不成为真人训练标签。
- 旧 `prompt_timeout` 没有完整 `chosen`，当前版本整局隔离。后续只有在对应历史工件与 adapter 能精确恢复其语义时才可登记。
- 输出动作按日志应用序号确定性排序，动作、磁带、准备结果和隔离记录都使用规范 JSON 的 SHA-256。
- checkpoint 事件使用 `grandumi.replay_checkpoint.v1`，必须完整覆盖 `opening + 每条磁带动作 + terminal`，动作 checkpoint 同时绑定 `actionOrderSeq` 与 `actionStableHash`。缺一条、重复、越过下一动作或终局字段不完整都会隔离。
- checkpoint digest 算法属于对应 artifact，由 `IReplayCheckpointProvider` 注入。worker 只生成实际值并与日志期望比较，禁止在验证时反向生成期望值。当前仓库只用合成／当前版本 fixture provider 证明执行路径，尚未冻结生产 digest provider。
- 整局 worker 有独立稳定等待超时、整局超时和取消信号；整局超时会取消在途进程内执行。成功结果保留 source/prepared/tape/contract/registry/artifact/worker/request/replay 的完整 hash lineage。

仍然 No-Go：

- 生产 registry 为空，旧日志也没有完整版本身份和显式 checkpoint contract，不能声称真实历史重放成功；
- 尚未归档、验证并登记独立历史二进制／规则包／卡表，也未实现独立进程 executable 启动与二进制复核；
- 当前在线日志尚未产生已冻结的 checkpoint contract；生产 checkpoint provider、动作前 observation、P0-B 合法动作集合、批量断点/manifest 和数据集导出仍未完成。

这些门禁完成前，不得把本层 fixture 结果称为可训练数据集，也不得判定 Gate B 已通过。
