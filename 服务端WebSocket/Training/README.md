# 确定性训练重放、不可变工件归档与隔离 worker 基础层

## 当前已经完成的边界

- 进程启动阶段冻结完整 `ReplayRuntimeIdentity`：Git commit、入口程序集 SHA-256、卡表内容清单、规则 manifest、RNG／确定性 ID／开局协议和重放配置版本；新对局只复用冻结身份。
- 完整 JSONL 对局可转换为“精确工件身份 + seed + 两副原始牌组 + accepted／系统动作磁带”；`IArtifactReplayWorker` dispatcher 按 descriptor 完整指纹路由，绝不回退到当前 `main`。
- 测试服可写 `grandumi.replay_checkpoint.v1`：开局、每条 accepted／系统动作后的稳定点和终局均绑定 full/public/random digest；任一缺失、重复、乱序或分歧都会整局隔离。
- 每次测试服后端构建现在会把完整 publish、当时的 `/data/grandumi-test/Rulesets` 快照和标准 manifest 归档到 `/var/lib/grandumi-test-replay-artifacts/<engineArtifactId>/`。
- 可从本机或服务器运行归档验证与日志覆盖审计；报告稳定区分 legacy、缺身份、缺 checkpoint、身份不匹配、artifact 未归档、可进入准备层和可进入独立 worker。

## 测试服不可变归档格式

最终目录只包含：

```text
<engineArtifactId>/
├── replay-artifact-manifest.v1.json
└── payload/
    ├── publish/        # 完整 dotnet publish 输出，包含卡表和 DSL
    └── rules/          # 部署时测试服规则包与 active-ruleset 快照
```

`grandumi.replay_artifact_archive.v1` manifest 冻结以下内容：

- 完整 `ReplayRuntimeIdentity` 与其 `manifestHash`；
- 全部目录、全部文件的规范相对路径、字节数、SHA-256 和 executable 标记；
- payload、publish、规则包三层聚合内容哈希；
- `GrandUMIServer.dll` 与卡表内容哈希，并逐字段交叉核对运行身份；
- 测试服实际服务入口：`/opt/dotnet/dotnet`、`GrandUMIServer.dll 8081`、工作目录 `payload/publish`；
- 独立 replay worker 状态。当前明确为 `available=false`，没有伪造 executable 或成功证据；
- `environment=test`、`productionRegistryEligible=false`，防止测试候选被误当作生产 registry。

manifest 使用规范 JSON 自哈希（计算时排除顶层 `manifestHash`）。验证器会拒绝未知字段、重复字段、非规范路径、路径穿越、Unicode／大小写冲突、符号链接或重解析点、缺文件、多文件、大小或哈希不符以及聚合身份不一致。

## 部署时序、不变量与失败恢复

测试服 `ops/server/deploy-test.sh` 的后端路径遵循：

1. 在 `publish.next` 构建完整输出；
2. 在归档根同一文件系统的 `.staging/capture-<pid>-<nonce>` 中稳定复制 publish 与规则包，两次扫描确认复制期间源未变化；
3. 生成 manifest，重新验证 manifest 自哈希、目录／文件集合、每文件与聚合哈希、入口程序集／卡表／规则身份；
4. 用目录 rename 发布为 `<engineArtifactId>`；最终目录在 rename 前不可见，残留 staging 永远不参与 catalog 或覆盖审计；
5. 同 artifactId 已存在时，只有目录集合、文件元数据和每个文件字节完全相同才幂等成功；无效或冲突归档一律失败且从不覆盖；并发发布由唯一 staging + 原子目录 rename 收敛为一个创建者和若干幂等读取者；
6. 用归档内历史 `GrandUMIServer.dll --replay-artifact verify-self` 重新加载归档卡表、DSL 和规则包，自证真实 `ReplayRuntimeIdentity`；
7. 原子切换 publish 与测试服专用环境文件后重启。服务启动会重新验证最终归档、当前 publish、当前规则目录和进程身份；缺 manifest 或任一不一致均 fail closed；
8. 就绪和版本检查通过后运行覆盖审计。任一归档、启动绑定或审计失败都会恢复上一版 publish 和上一份环境文件并尝试重启旧服务。冲突归档本身保留用于排查，不会被静默删除或覆盖。

测试服 service 仅以只读方式访问 `/var/lib/grandumi-test-replay-artifacts`。候选服与正式服 service／部署脚本没有接入该路径，也没有启用该要求。

## 命令入口

命令必须由对应 publish 中的 `GrandUMIServer.dll` 执行。测试服部署脚本会自动调用；本地验证仍须遵守仓库 E 盘临时目录规则。

```bash
/opt/dotnet/dotnet GrandUMIServer.dll --replay-artifact capture \
  --publish-root /path/to/publish \
  --rules-root /path/to/Rulesets \
  --archive-root /var/lib/grandumi-test-replay-artifacts \
  --engine-commit <40位小写提交号>

/opt/dotnet/dotnet GrandUMIServer.dll --replay-artifact verify \
  --archive /var/lib/grandumi-test-replay-artifacts/<artifactId> \
  --dotnet /opt/dotnet/dotnet

/opt/dotnet/dotnet GrandUMIServer.dll --replay-artifact audit \
  --logs /data/grandumi-test/MatchLogs \
  --archive-root /var/lib/grandumi-test-replay-artifacts \
  --json /var/lib/grandumi-test-release/replay-coverage.v1.json \
  --markdown /var/lib/grandumi-test-release/replay-coverage.v1.md \
  --candidate-catalog /var/lib/grandumi-test-release/test-replay-artifact-candidates.v1.json \
  --dotnet /opt/dotnet/dotnet
```

`verify` 先用当前验证器检查不可变文件集合，再启动归档内历史程序集执行 `verify-self`。因此规则 manifest 不是只在外层 JSON 中互相抄写，而是由历史代码、历史 DSL 和归档规则包重新计算并与归档身份核对。

## 覆盖审计状态

JSON 使用 `grandumi.replay_coverage_report.v1`，没有时间戳；输入内容与归档 catalog 不变时，条目顺序、条目哈希、报告哈希和 Markdown 字节均稳定。所有状态始终显式输出计数，包括 0。

| 稳定代码 | 含义 |
|---|---|
| `legacy` | 使用 legacy accepted-pairing adapter；不冒充当前可训练日志 |
| `missing_identity` | `match_start` 缺任一精确身份字段或 `replayRuntimeManifestHash` |
| `missing_checkpoint` | 身份与归档匹配且动作准备成功，但没有完整 checkpoint contract |
| `identity_mismatch` | artifactId 存在，但日志任一身份字段与归档不一致 |
| `artifact_not_archived` | 日志身份完整，但没有对应的已验证最终归档 |
| `preparation_ready` | 身份、artifact、动作磁带和 checkpoint contract 均通过，可进入准备层；当前仍没有独立 worker |
| `replay_worker_ready` | 只有独立进程 worker 入口和协议真实可用且通过验证时才可计数；当前必为 0 |
| `invalid_log` | 其他结构、序号、动作配对或 checkpoint 契约错误 |

部署会另写 `grandumi.test_replay_artifact_candidates.v1`。它是测试服候选 catalog，不是 `ReplayArtifactRegistry`，并明确记录 `productionRegistryModified=false`、worker unavailable 和生产登记 No-Go。

## 既有重放冻结规则

- `grandumi.matchlog.v1.accepted-self-contained.v2` 强制每条 accepted 自包含规范 `data`、`requestId` 和真实 `source`；requested 只用于相关性与篡改审计。rejected 不进入磁带，`source=system` 只推进重放而不成为真人标签。
- `grandumi.matchlog.v1.accepted-pairing.v1` 只接纳能唯一配对的历史 requested → accepted；v2 不回退旧语义。旧 `prompt_timeout` 缺完整 `chosen` 时整局隔离。
- checkpoint digest 属于对应 artifact。worker 只能核对日志期望，不能反向生成期望；恢复日志会写连续性停用标记，禁止拼接恢复前后的部分 checkpoint。
- checkpoint 行不持久化原始 GameState、账号、显示名、session、完整卡组、隐藏区、Prompt 私有候选或 replayHands。排队成功不等于落盘成功，缺行最终仍由完整契约 fail closed。
- dispatcher 会复算 response 规范哈希，并核对 source、prepared、tape、contract、registry、artifact、worker、request 和 replay lineage；超时和取消都只返回整局隔离，不返回部分样本。

## 仍然 No-Go

- `Training/Artifacts/replay-artifact-registry.v1.json` 仍为空。本阶段没有把测试服候选写入生产 registry。
- 本阶段建立的是可取回、可自校验的历史运行工件及验证器；独立进程 replay worker 协议入口尚未实现，也没有真实日志通过独立 worker 的证据。因此 `replay_worker_ready` 必须为 0，不能宣称 Gate B 已通过。
- 代码完成不等于服务器已产生归档或覆盖报告；只有本提交后续真实部署测试服并运行命令，服务器持久路径才会出现证据。没有新 checkpoint 日志时报告必须如实为 0。
- 测试服运行中另行激活或新增规则热更新包不会自动创建新归档；这类新身份会在审计中成为 `artifact_not_archived`。下一阶段必须把规则激活与同样的不可变归档门禁绑定后，才可允许其进入 worker。
- 当前归档仅在测试服本机持久路径可取回，尚未建立异机冗余、保留期／容量治理和灾备恢复演练。
- 动作前 observation、P0-B 合法动作集合、批量断点／训练 manifest 和数据集导出仍未完成。

这些门禁完成前，不得把 fixture、`preparation_ready` 或测试候选 catalog 称为可训练数据集，也不得判定真实历史重放成功。
