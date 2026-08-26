# GrandUMI AI 真人对练完整版训练方案

> 文档状态：实施方案，不代表任何训练、上线或胜率目标已经完成  
> 目标：在严格冻结的评测口径下，使 AI 对随机抽样、账号去重的真人玩家总体胜率 Wilson 95% 下置信界达到 60%  
> 最高优先级：P0-A 确定性重放训练导出器；P0-B 统一合法动作枚举器／合法动作掩码  
> 依据日期：2026-08-26

## 0. 先冻结的业务不变量与结论

后续所有 issue、实现和评审必须共同维护以下不变量：

1. **同一历史对局必须由当时的引擎、卡牌数据、卡效规则、开局协议、随机算法和动作磁带重建。** 不允许用当前版本“尽量重放”旧规则，也不允许在分歧后继续产样本。
2. **只重放实际被接受的状态动作。** rejected 请求只用于数据质量、攻击审计和错误分析，不得成为策略标签；超时自动选先后手、自动保留调度等服务端动作必须作为可重放的系统动作进入磁带，但不得伪装成真人决策样本。
3. **每条训练样本都取自动作执行前的稳定状态。** 样本顺序固定为：等待引擎稳定 → 构建 actor 当时可见 observation → 枚举合法动作／生成 mask → 核对真人动作在集合内 → 执行动作 → 等待下一稳定点。
4. **训练与线上推理只能看 actor 当时可见的信息。** 服务端可在受控重放进程持有完整状态，但不得把对手手牌、生命暗牌、牌库顺序、未来随机结果、对手完整卡组或终局后的回放手牌时间线写入 policy 输入。
5. **合法动作只有一个规则真源。** 枚举、解释非法原因、HandleAction 最终校验、离线导出和线上 AI 都调用同一组规则；不得分别维护“训练合法性”和“实战合法性”。
6. **所有集合、实例标识和 mask 都必须确定性排序。** 同一状态重复枚举得到逐项相同的 actionId、顺序和 legalSetHash；不得依赖 Dictionary、HashSet 或文件遍历的偶然顺序。
7. **任何未通过重放一致性、动作覆盖、隐私扫描或数据切分检查的对局都隔离。** 宁可减少样本，不把静默错误送进模型。
8. **60% 是评测结论，不是训练集指标。** 离线模仿准确率、自博弈胜率或击败当前 Bot 都不能替代真人、去重账号、预注册口径下的验证。

这两个 P0 是所有正式训练的阻塞前置：

- 没有 P0-A，就无法从当前生产日志重建动作前的玩家视角状态，现有样本实际上没有可用 observation。
- 没有 P0-B，就无法知道可选动作、无法做安全 mask、无法判断历史标签是否仍合法，也无法保证模型输出能被引擎接受。
- 可以做模型代码脚手架或合成数据实验，但在两个 P0 共同通过退出门槛前，不得将真实日志训练结果晋级到影子或真人测试。

## 1. 执行摘要

### 1.1 数据数量足够做第一代强策略，当前瓶颈不是局数

近期业务统计显示已有数万局有效对局。这个数量级适合判断业务规模，但它来自排行榜／业务统计，**不能写成已经齐备、去重、可重放的原始日志数**。

已完成的新独立安全快照保存在受控内部备份存储中，具体路径、时间戳和精确容量不进入公开仓库。

快照说明确认其中包含数万份日期范围互不重叠的 JSONL 日志和 GB 级备份载荷；其他历史备份必须另行按内容与对局指纹去重，不能直接相加。对数百局日志的只读抽样覆盖数万条动作请求、accepted／rejected 结果、prompt_created／response 以及少量历史 timeout。事件计数并非严格一请求一结果，说明历史自动动作或日志路径存在额外事件，P0-A 必须按事件序列适配，不能只做简单 join。

按抽样平均值粗略外推：

- 数万局日志约对应数百万条 accepted 状态动作；
- 业务统计覆盖的全部对局约对应近千万量级 accepted 状态动作；
- prompt 响应预计达到百万量级。

这些只是容量估算，最终决策样本数取决于完赛过滤、版本可复现率、真人模式过滤、动作前稳定点、重复备份去重和隐私门禁。即使只保留其中一部分，数量通常也足以训练第一代结构化行为克隆模型。真正的瓶颈是：**能否正确重建每个决策点，以及能否为该状态生成与执行同源的合法动作集合。**

### 1.2 当前最小导出器不能直接训练

现有 tools/export-training-samples.mjs 的逻辑是记住最近一次 private_snapshot，在 player_action_requested 时直接填入 observation 和 hiddenState，并把 legalActions 固定为空数组。对一批真实日志的实测导出结果为：

- 全部 observation.player 为 null；
- 全部 hiddenState 为 null；
- 全部 legalActions 为空；
- 只有终局结果可用。

原因不是局数不足，而是生产环境 GRANDUMI_PRIVATE_SNAPSHOT_LOG=0。新生产日志抽样中未发现 private_snapshot。当前 public_snapshot 是观战脱敏视角，也不能恢复 actor 自己的手牌等私有可见信息。

### 1.3 60% 可行，但不能提前承诺

GrandUMI 已有确定性 seed、确定性卡实例 ID、MatchReplay 重建骨架、规则集按局锁定、动作 accepted／rejected 日志、prompt 候选与响应、随机事件和公开快照，这显著降低了从零建设成本。数万局历史对局提供了足够的行为覆盖，因此“击败 60% 真人玩家”是合理工程目标。

但能否达到仍取决于：

- 支持版本的重放覆盖率；
- 排位／玩家水平标签能否按对局时点正确联结；
- 领袖、卡组和动作长尾；
- 真人日志本身的策略质量；
- 评测玩家分布、AI 卡组选择和先后手分布；
- 仅行为克隆是否达到瓶颈，是否需要 value、搜索或后续自博弈。

因此路线应以可验证的 Go／No-Go 门推进，不承诺固定日期或一次训练必达 60%。

## 2. 成功指标与“打败 60% 玩家”的严格口径

### 2.1 主指标

建议把对外可声称的目标冻结为：

> 在指定对战格式、rulesVersion、cardDbContentHash、AI 模型版本、AI 合法卡组池和评测时间窗内，对从目标活跃玩家总体中随机抽样、按规范化账号去重的真人对手，AI 的严格胜利比例 Wilson 95% 下置信界不低于 60%。

主分析先从目标总体中按规范化账号去重并随机抽取真人，再按账号等权，而不是让对局数多的账号获得更大权重：

- 每个入选账号的对局数设置预注册上限；为了交换先后手或镜像卡组可以配对进行多局；
- 点估计先在账号内求严格胜利比例，再对账号等权汇总；
- 区间同时报告账号聚类 bootstrap／cluster-robust 结果和 Wilson 独立局近似；
- 对重复账号、镜像 seed 和配对设计按设计效应折减有效样本量，不能把总对局数直接当独立 n；
- 不允许让高频、较弱或愿意反复挑战的少数玩家主导样本。

严格胜利比例按二项口径计算：

- AI 胜利记 1；
- AI 失败、平局、AI 投降、AI 操作超时、AI 推理服务故障导致的兜底判负均记 0；
- 这样平局不会被排除后人为抬高胜率，Wilson 二项区间也有明确含义；
- 同时另报传统的 胜／负／平、只看分胜负对局的胜率、超时率和投降率。

### 2.2 样本量与置信要求

有限样本中观测胜率等于 60% 时，Wilson 下界必然低于 60%，所以验收不能写死一个局数后自动通过。按独立二项对局近似，达到“双侧 95% Wilson 下界不低于 60%”所需的最小局数约为：

| 观测胜率 | 最小独立对局数近似 | 解释 |
|---:|---:|---|
| 61% | 9,220 | 点估计只高 1 个百分点，必须大规模扩样 |
| 62% | 2,305 | 适合较保守、较大规模的正式验证 |
| 63% | 1,025 | 千局级刚达到统计门槛 |
| 64% | 577 | 仍需检查账号和分层覆盖 |
| 65% | 369 | 统计局数较少，分层置信度仍有限 |

正式统计至少先冻结 **1,000 场以上有效真人对局、300 个以上去重真人账号**。这只是启动正式判定的最低数据覆盖，不是自动通过条件：

- 若点估计不足 63%，继续扩样到上表对应的 Wilson 门槛；
- 若点估计为 63%，名义独立局也至少需要约 1,025 场，不能把 1,000 场四舍五入为通过；
- 同账号重复、先后手镜像、同 seed 配对会相关，必须用预注册的设计效应或账号聚类方法得到更小的有效样本量，再计算保守 Wilson 下界；
- 最终要求账号聚类置信下界和按有效样本量计算的 Wilson 下界都不低于 60%；
- 即使总体过线，也不能据此宣称主要领袖、先后手或段位都达到 60%，这些分层必须分别给样本量和区间。

不得在看过结果后修改最小样本量、有效样本量折减、排除规则或目标总体。

### 2.3 目标总体和随机化

评测开始前必须预注册：

- 对战格式：标准／狂野／休闲／排位中的哪一个或哪些；
- 目标玩家：例如最近 30 天至少完成 N 场真人对局的活跃账号；
- AI 卡组策略：固定单卡组、冻结的多卡组池，或按公开规则随机分配；不得根据对手账号或已知卡组临场反制；
- 人类卡组：玩家正常选择的合法卡组；
- 先后手：AI 先手与后手各约 50%，由服务端随机；
- 领袖与段位抽样配额：总体按真实活跃账号分布加权，同时保证核心分层有最低样本；
- 模型、推理参数、actionSpaceVersion、引擎与规则内容哈希；
- 开始／结束条件和所有排除条件。

### 2.4 特殊结果处理

| 情形 | 主指标处理 | 额外报告 |
|---|---|---|
| 正常终局 | 胜为 1，负为 0 | 终局原因 |
| 真人主动投降 | 若已进入有效对局，计 AI 胜 | 按回合数分层 |
| AI 主动投降 | 计 AI 负 | 模型原因／兜底原因 |
| 双方同意 Bug 平局 | 计非胜 0 | 单列平局和关联版本 |
| AI 推理超时／服务故障 | 计 AI 非胜；若规则判负则为负 | 推理错误率、兜底动作率 |
| 真人操作棋钟超时 | 对局已正常开始则按引擎结果计 | 人类超时率 |
| 开局前断线、无有效动作 | 按预注册的无效对局规则排除 | 数量和账号，禁止事后选择 |
| 中途断线并被规则判负 | 按引擎结果计 | 断线率 |
| Bot／GM／Debug 对局 | 排除 | 单独用于工程测试 |
| 同账号重复对局 | 主指标只保留预选 1 局 | 次要账号等权结果 |
| 版本切换中的旧局 | 按该局锁定版本统计 | 各 rulesVersion 单列 |

### 2.5 分层报告

即使总体通过，也必须同时报告：

- AI 先手／后手；
- AI 领袖、人类领袖、领袖对阵矩阵；
- 标准／狂野、休闲／排位等 matchKind；
- 真人对局时段位、rating 分位或活跃度层；
- rulesVersion、cardDbContentHash、模型版本；
- 正常终局／投降／超时／平局；
- 已见卡组 archetype 与长尾／新卡组；
- 推理延迟、兜底率、非法候选拦截率。

总体下界达到 60% 是主门槛；各分层至少要有置信区间和样本量，不能用样本过少的分层高胜率宣传“全面 60%”。

### 2.6 四级评测门

1. **离线门**：重放、合法性、数据泄漏、top-k／NLL／校准和历史动作覆盖通过。
2. **引擎门**：镜像 seed／卡组／先后手对局中稳定击败 BotDriver、脚本基线和上一模型，零非法执行。
3. **影子与封测门**：只预测不落子，再进入内部／受邀真人封测；确认延迟、卡死、漏洞和分层退化。
4. **线上主指标门**：按预注册总体先完成至少 1,000 场有效局、300 个去重真人账号，并继续扩样，直到账号聚类区间和按有效样本量计算的 Wilson 95% 下界都达到 60%。

## 3. 当前资产、数据流与差距

### 3.1 代码中的现有数据流

~~~text
客户端 MsgGameAction
  → GameRoomManager.HandleAction
  → requestId 去重 + 房间 ActionQueue 串行化
  → 记录 player_action_requested
  → GameEngine.HandleAction
      → 各动作内校验／ActionValidator
      → accepted：player_action_accepted + OnPersistAction
      → rejected：player_action_rejected
      → 异步效果链／PromptSystem
      → Broadcast 构建双方玩家视角与观战快照
  → MatchLogRecorder append-only JSONL
~~~

关键现有落点：

- 服务端WebSocket/Game/MatchReplay.cs：seed + deckRaw + 有序 ActionEntry 重建，并在每步调用 WaitSettledAsync；支持 ruleset 固定、开局协议兼容、AlwaysPromptOnLifeReveal 和旧 RequestDraw 迁移。
- 服务端WebSocket/Game/GameEngine.cs：HandleAction 是状态动作入口；accepted 才调用 OnPersistAction；随机行为走 GameState.Rng；卡实例 ID 由 seed 派生；Broadcast 记录 public_snapshot，私有快照由开关控制。
- 服务端WebSocket/Game/GameRoomManager.cs：房间动作队列、请求去重、日志事件、超时自动动作、vsBot 接入和恢复日志；生产私有快照默认关闭。
- 服务端WebSocket/Effects/PromptSystem.cs：promptId 为单调确定性序号；prompt_created 包含 validChoices、min/max、extra，PromptResponse 有严格数量、去重和候选校验。
- 服务端WebSocket/Game/Validation/ActionValidator.cs：已有纯检查方法，但只覆盖 EndTurn、PlayCard、AttachDon、Attack、UseEffect、DeclareBlocker 等部分动作。
- 服务端WebSocket/Game/Snapshot/StateSnapshotBuilder.cs：能生成双方各自可见的玩家快照，但包含面向网络和展示的字段，不等同于脱敏训练 observation。
- 服务端WebSocket/Game/Snapshot/PrivateStateSnapshotBuilder.cs：可做内部审计参考，但生产未常态记录，且训练 policy 不应消费其隐藏信息。
- 服务端WebSocket/Effects/Rules/CardRuleset.cs：每局锁定不可变 rulesetId；内置版本为 builtin-构建提交号，热更新包按 baseRulesetId 继承。
- 服务端WebSocket/Cards/CardDatabase.cs：从 卡牌数据/*.json 加载，但 match_start 目前只写 cardDbVersion=local-card-json，没有内容哈希。
- 服务端WebSocket/Game/BotDriver.cs：当前 Bot 固定为 P1，只做最小推进；收到玩家视角状态后，经 EnqueueBotAction 回到同一房间动作队列。
- 服务端WebSocket/Game/Ranked/RankedStore.cs：ranked_matches 和 rank_rating_events 可提供对局结果、对局时 rating／RP 联结基础，但必须在受控环境按 matchId、账号键和对局时点连接，不能把未来段位泄漏进训练特征。

### 3.2 数据资产与差距表

| 项目 | 已确认资产 | 差距／影响 |
|---|---|---|
| 最近业务规模 | 数万局有效对局 | 业务统计，不等于日志已齐备、去重、完赛或可重放 |
| 新独立快照 | 数万份非重叠日期 JSONL，GB 级 | 日期不连续；旧备份需另行内容去重；生产仍在继续写 |
| 抽样结构 | 数百局、数十万行；全部 JSON 合法、seq 严格递增、matchId 一致 | 绝大多数含终局；未终局和活跃追加文件必须隔离或后续增量补齐 |
| 动作事件 | 数万条 requested／accepted，少量 rejected | 计数不严格守恒；accepted 只记 action，不带原始 data，需要序列适配与未来 schema 补强 |
| Prompt | 万级 created／response，少量历史 timeout；全部 prompt 有 validChoices | created／response／timeout 仍有缺口；当前 PromptSystem 已改为等待明确响应，旧 timeout 需版本适配 |
| 普通 legalActions | 无 | 无法训练 masked policy，也无法核对历史动作 |
| 私有快照 | 测试／排障代码存在 | 新生产抽样中未发现，不能靠当前最小导出器获得 actor 手牌 |
| 公开快照 | public_snapshot 丰富，且代码能构建双方玩家视角 | 日志保存的是观战视角；对手隐藏正确脱敏，但也缺 actor 自己的私有可见信息 |
| 开局重建 | match_start 有 deckRaw、seed、firstPlayer、opening 标志、matchKind、rulesVersion | AlwaysPromptOnLifeReveal 等重放配置未完整写入 match_start；历史自动动作需要 adapter |
| 随机性 | seed、randomSeq、骰点、shuffle 前后顺序；GameState.Rng 与确定性 ID | 仍应固定 RNG 算法／运行时版本；旧版本可能有未进入统一 RNG 的路径 |
| 规则版本 | rulesVersion 可锁定 builtin commit 或热更新包 | 必须保留对应服务端产物、基础规则包和插件；只有 ID 不保证包仍可取 |
| 卡牌数据版本 | 当前字符串 local-card-json | 不能证明 卡牌数据 内容一致；必须增加规范化内容哈希与归档 |
| 重放骨架 | MatchReplay 和 ReplayEquivalenceTests 已证明同 seed／同磁带可得到相同快照 | 这是恢复能力，不是批量训练导出器；尚无历史版本注册、事件 adapter、断点和质量报告 |
| 排位／水平标签 | RankedStore 有 match 与 rating events；玩家快照可含开局段位身份 | 日志备份与排位库未证明齐备联结；必须 as-of join，禁止使用终局之后或当前段位 |
| 模式标签 | matchKind 区分 Ranked、Casual、Bot、Friendly 等 | 旧 UnknownHuman 需谨慎分类；Bot／Debug／GM 必须排除真人训练 |
| 偏差 | 大量真实行为、丰富 prompt | 高频玩家、热门领袖、较弱玩家、投降／超时、版本和长局会过度代表 |
| 当前导出 | 实测样本均无 observation、hiddenState、legalActions | 明确 No-Go：不能直接进入任何正式训练 |

本次新快照的有效载荷文件有独立 SHA-256 清单。旧备份目录不属于本次新快照，其中一个旧包的已记录 SHA-256 与非空文件事实矛盾；在重新建立可信内容身份和来源证明前，不得把该旧包并入训练 source manifest。其他旧包即使通过已记录复核，也仍应走相同的去重和版本门禁。

### 3.3 旧规则复现风险

rulesVersion 解决的是“卡效规则对象按局锁定”，但确定性重放还依赖：

- 服务端核心 GameEngine、BattleEngine、TurnEngine、AtomicOps 和状态结构；
- 卡牌数据文件的逐字节或规范化内容；
- DSL definitions、手写脚本程序集和热更插件；
- .NET 运行时以及 System.Random 的行为；
- 开局流程版本、确定性 ID 算法、AlwaysPromptOnLifeReveal、matchKind 相关开关；
- 历史日志 schema 和自动动作语义。

任何一项缺失，都不能静默套用新版本。对应对局应标记 unsupported_version 或 replay_diverged，不进入训练。

## 4. P0-A：确定性重放训练导出器

### 4.1 目标与非目标

目标：

- 从受控原始日志和精确版本产物重建动作前稳定状态；
- 只对 accepted 真人决策生成 actor 可见的 training_sample.v2；
- 为每条样本生成 P0-B 的合法动作集合和 mask；
- 支持批量、断点、幂等、去重、质量报告和 lineage；
- 任一分歧即隔离整局，不输出“部分可信”样本。

非目标：

- 不在实时对局线程生成训练样本；
- 不把 private_snapshot 重新设为生产常态依赖；
- 不修补历史引擎使旧动作“看起来合法”；
- 不在导出产物中保留真实账号、显示名、会话 ID 或完整隐藏状态。

### 4.2 建议模块

建议优先用 C# 实现重放核心，直接复用 GameEngine、MatchReplay、CardRuleset 和 P0-B；Node 工具只负责清单、分片和质量汇总。

~~~text
服务端WebSocket/Training/
  ReplayArtifactRegistry.cs
  MatchLogEventAdapter.cs
  AcceptedActionTapeBuilder.cs
  TrainingObservationBuilder.cs
  ReplayConsistencyChecker.cs
  TrainingSampleExporter.cs
  TrainingSampleSchema.cs
  DatasetManifest.cs

tools/
  export-training-dataset.mjs
  verify-training-dataset.mjs
~~~

现有 tools/export-training-samples.mjs 保留为 v1 兼容或明确废弃提示，不应继续成为正式入口。

### 4.3 输入与版本注册表

每个可重放版本必须在不可变注册表中登记：

| 字段 | 含义 |
|---|---|
| matchlogSchema | grandumi.matchlog.v1 及其历史 adapter 版本 |
| engineArtifactId | 服务端发布产物 ID |
| engineCommit／binarySha256 | 核心引擎精确提交与二进制哈希 |
| rulesVersion | 日志锁定的 builtin 或热更规则 ID |
| rulesetManifestHash | 规则包 manifest、definitions、插件集合的内容哈希 |
| cardDbContentHash | 对 卡牌数据 下规范化相对路径与文件字节计算的 SHA-256 |
| rngAlgorithmVersion | 明确算法与运行时；不要只依赖“seed 相同” |
| deterministicIdVersion | 卡实例 ID 派生算法版本 |
| openingProtocolVersion | 是否延迟开局、骰点／先后手／发牌顺序 |
| replayConfigSchema | AlwaysPrompt、leaderKeywordWildcard 等影响动作磁带的配置 |
| executable | 对应重放 worker 镜像或进程入口 |

注册表只接受不可变 artifact。规则包和卡牌数据必须连同哈希归档；缺少对应 artifact 时立即 quarantine，不回退到当前 main。

未来 match_start 至少补充：

- engineArtifactId、engineCommit；
- cardDbContentHash；
- rulesetManifestHash；
- rngAlgorithmVersion、deterministicIdVersion；
- openingProtocolVersion；
- p0／p1 AlwaysPromptOnLifeReveal；
- 所有会改变规则状态或 prompt 链的房间配置。

### 4.4 历史事件适配与 accepted 动作磁带

当前日志中 player_action_requested 带 action 和 data，player_action_accepted 只带 action。历史 adapter 应：

1. 按 seq 顺序读取，校验 schema、matchId、单调 seq 和完整尾行；
2. 建立未决 requested 队列，按 actor、action 和时序把 accepted／rejected 配对；
3. 只把已配对 accepted 的原始 data 进入磁带；
4. 对 starting_player_choice_timeout_auto_select、mulligan_timeout_auto_keep 等无普通 requested 的系统动作，使用版本化 adapter 生成 system action；
5. 对 PromptResponse 同时核对 prompt_response 事件的 promptId／chosen；
6. 重复 requestId 已在房间层去重，日志中若仍发现完全重复 accepted，按状态和事件指纹判定，不能盲删；
7. 无法唯一配对、缺 data 或事件顺序冲突时隔离整局。

未来应把 accepted 事件升级为带完整规范化命令：

~~~json
{
  "kind": "player_action_accepted",
  "actor": 0,
  "payload": {
    "requestId": "dataset-never-exports-this",
    "action": "Attack",
    "data": {
      "attackerId": "instance-id",
      "targetIsLeader": true
    },
    "source": "player"
  }
}
~~~

训练导出时仍删除 requestId，并把原始实例 GUID 映射为对局内、视角内的短 token。

### 4.5 重放与采样算法

每局固定执行：

1. 计算源文件 SHA-256、match fingerprint，做备份去重并选择最长完整版本；
2. 读取 match_start、match_end 和完整事件流；
3. 解析并验证版本注册项；
4. HMAC 伪名化账号，仅在受控 staging 中保留 splitGroupKey；
5. 从 accepted／系统事件构建动作磁带；
6. 以 deckRaw、rngSeed、firstPlayer、开局标志、规则集和配置创建静默 GameEngine；
7. 开局后 WaitSettledAsync，到达稳定状态；
8. 对磁带中每个动作：
   - 若为真人策略动作，先用 TrainingObservationBuilder 构建 actor 视角；
   - 调 P0-B 生成 LegalActionSet 和 legalSetHash；
   - 把历史 wire action 规范化为稳定实例 ID 动作；
   - 断言 actionTaken 在合法集合／约束内；
   - 暂存样本，不立即提交；
   - 调同源执行入口应用动作，断言 HandleAction／ApplyValidated 返回 accepted，再 WaitSettledAsync；现有 MatchReplay 未替批量导出器做这一训练门禁，P0-A 必须显式补上；
   - 校验 prompt、random_event、公开快照 checkpoint 和状态 digest；
   - 若动作为系统自动动作，只应用和校验，不生成真人标签；
9. 对齐 match_end 的 winner、draw、reason、turn 和终局 digest；
10. 只有整局通过，才原子提交该局全部样本和 manifest；否则删除暂存分片并写 quarantine reason。

决策点包括 ChooseFirstPlayer、Mulligan、主要阶段、攻击、阻挡、反击、UseEffect 和 PromptResponse。Surrender、RequestDraw／RespondDraw、操作时钟控制可保留在审计流，但默认不进入游戏策略 policy：投降和平局是产品／安全策略，PlayerActivity 和 RequestTurnExtension 也不属于 GameEngine 卡牌动作空间。

### 4.6 动作前 observation 与隐藏边界

新建 TrainingObservationBuilder，不直接序列化 PrivateStateSnapshotBuilder，也不原样输出网络 MsgGameState。它可复用 StateSnapshotBuilder 的可见性规则，但字段白名单固定：

允许：

- actor 自己的领袖、手牌卡号与实例 token、场上、舞台、废弃区、公开生命、各区数量、咚状态；
- actor 已知的自己的卡组构成，但不能包含当前牌库顺序；是否加入第一版由模型 schema 冻结；
- 对手领袖、公开场面、舞台、废弃区、公开生命、手牌／牌库／生命数量；
- turn、phase、first player、当前战斗、actor 自己的 pendingPrompt 和候选；
- 当时已公开的持续效果、限制、卡牌当前数值；
- match format 和规则版本等非身份元数据。

禁止：

- 对手手牌卡号／实例、暗置生命、牌库顺序、完整卡组；
- actor 自己未知的牌库顺序和未来 random_event；
- 账号、显示名、sessionId、聊天、requestId；
- 终局 replayHands 时间线；
- 对局之后的段位、最终胜负以外的未来状态；
- private_snapshot 或其可逆压缩形式。

内部一致性 checker 可访问完整状态，但 hidden audit sidecar 与 policy dataset 必须物理分开、不同权限、不同 manifest。正式 policy 文件不提供 hiddenState 字段，避免“训练时剥离”被漏做。

### 4.7 training_sample.v2 示例

以下只展示必要结构，ID 均为数据集内 token，不是真实账号或原始 GUID：

~~~json
{
  "schema": "grandumi.training_sample.v2",
  "datasetId": "human-bc-0001",
  "decisionId": "m_7f2a:84",
  "source": {
    "matchToken": "m_7f2a",
    "sourceSeq": 312,
    "sourceFileHash": "sha256:...",
    "replayDigest": "sha256:..."
  },
  "actorSeat": 0,
  "observation": {
    "turn": 5,
    "phase": "Main",
    "firstPlayerSeat": 1,
    "self": {
      "leader": {"instanceId": "self_leader", "cardNumber": "OP00-000"},
      "hand": [{"instanceId": "self_h2", "cardNumber": "OP00-101"}],
      "deckCount": 31,
      "lifeCount": 3,
      "activeDon": 4
    },
    "opponent": {
      "leader": {"instanceId": "opp_leader", "cardNumber": "OP00-001"},
      "handCount": 6,
      "deckCount": 29,
      "lifeCount": 4,
      "characters": []
    },
    "battle": null,
    "prompt": null
  },
  "legalActionSet": {
    "actionSpaceVersion": "grandumi.action.v1",
    "legalSetHash": "sha256:...",
    "familyMask": [0, 1, 1, 0, 1],
    "actions": [
      {
        "actionId": "attack:self_leader:opp_leader",
        "family": "Attack",
        "params": {
          "sourceId": "self_leader",
          "targetId": "opp_leader",
          "targetIsLeader": true
        }
      },
      {
        "actionId": "end_turn",
        "family": "EndTurn",
        "params": {}
      }
    ]
  },
  "actionTaken": {
    "actionId": "end_turn",
    "family": "EndTurn",
    "params": {}
  },
  "outcome": {
    "actorScore": 1,
    "terminalReasonCategory": "normal"
  },
  "metadata": {
    "matchKind": "Ranked",
    "rulesVersion": "builtin-...",
    "cardDbContentHash": "sha256:...",
    "engineArtifactId": "server-...",
    "leaderNumber": "OP00-000",
    "opponentLeaderNumber": "OP00-001",
    "rankBucketAtMatch": "bucket_3"
  },
  "quality": {
    "replayVerified": true,
    "actionInLegalSet": true,
    "privacyScanPassed": true,
    "weight": 1.0
  }
}
~~~

### 4.8 幂等、断点、去重与 lineage

数据集运行键建议为：

~~~text
datasetId + exporterCommit + schemaVersion + registryVersion + sourceManifestHash
~~~

每局输出到确定性分片名，内容先写 E:\GrandUMI-Temp\ 下的任务目录，再校验、原子移动到最终数据区；不得使用 C 盘临时目录。任务结束清理自身临时产物。

manifest 至少记录：

- 所有输入包／文件哈希、快照截止点和恢复叠加顺序；
- 重复对局选择规则和被舍弃来源；
- exporter、引擎 artifact、规则、卡牌库、action space 哈希；
- 总局数、完赛数、过滤数、隔离原因；
- 每版本重放成功率、每动作域样本数；
- split 算法、HMAC keyId、时间窗；
- 每个输出 shard 的行数、哈希和 schema；
- 可从 sample 追到源文件与 seq，但不能从公开数据集反推出账号。

重复备份处理：

- 先按源文件 SHA-256 去重；
- 再按 matchId + rngSeed + 双方 deck 内容哈希 + 首个／末个事件摘要构造 match fingerprint；
- 同 matchId 的追加前缀保留最长且结构完整、终局更完整的版本；
- 两份同 matchId 内容不是前缀关系时标记 conflict，不自动拼接。

### 4.9 重放一致性验收

单局是全有或全无：

- accepted／系统动作全部成功应用；
- randomSeq、随机事件类型和结果 100% 对齐；
- promptId、actor、kind、validChoices、min/max 和 response 100% 对齐；
- 每个可比 public_snapshot 的规范化公开状态 hash 对齐；
- 有 private_snapshot 的测试／历史局，规范化完整状态 hash 对齐；
- match_end 结果、回合、平局与原因类别对齐；
- 历史 actionTaken 在 P0-B 合法集合中；
- 隐私扫描通过。

数据集级退出门槛：

- 注册为 supported 的版本金丝雀／golden corpus：100% 逐 checkpoint 一致；
- 最新主版本完整真人日志：至少 99.5% 可重放；
- 所有目标版本合计：至少 95% 可重放；低于此值先修 adapter／补 artifact，不开始正式训练；
- 任何分歧局 0 条样本进入数据集；
- 重跑同一 manifest，shard 内容哈希 100% 相同。

### 4.10 P0-A 测试矩阵

| 维度 | 必测场景 |
|---|---|
| 开局 | 固定先手、骰点、同点重骰、选择先／后手、选择超时自动先手、延迟开局协议 |
| 调度 | 双方保留、单／双重抽、调度超时自动保留 |
| 主阶段 | 出角色／事件／舞台、角色区满员新旧两种流程、赋予 1～N 咚、UseEffect、EndTurn |
| 战斗 | 领袖／角色攻击、目标领袖／角色、攻击税 prompt、无阻挡自动跳过、Declare／PassBlock |
| 反击 | 反击值、反击事件、多次反击、PassCounter、伤害、生命触发 |
| Prompt | min=0、min>0、max>1、排序、Option、LifeTrigger、成本返回确认、重复／伪造候选拒绝 |
| 取消／重复 | optional decline、旧 prompt 重复响应、requestId 重发、rejected 无状态副作用 |
| 终局 | 正常胜负、投降、平局申请接受／拒绝、牌库耗尽、操作／断线超时 |
| 版本 | builtin、热更规则包、旧 RequestDraw、缺规则包、卡表 hash 不符、未知 schema |
| 日志边界 | 无 match_end、追加中尾部、seq 跳号、重复文件、冲突副本、accepted 缺 data |
| 隐私 | 双方视角字段白名单、终局 replayHands 不进入 observation、账号与完整卡组扫描 |
| 确定性 | 相同 manifest 逐字节相同；不同 seed 状态不同；多 worker 并发顺序不影响分片内容 |

## 5. P0-B：统一合法动作枚举器／合法动作掩码

### 5.1 当前问题

ActionValidator 已提供多项纯校验，但合法性仍分散：

- PlayCounter、PromptResponse、Mulligan、ChooseFirstPlayer、PassBlock／PassCounter 等规则直接写在 GameEngine handler；
- StateSnapshotBuilder 只计算部分按钮可用性；
- BotDriver 通过手写 if 推测下一动作；
- HandleAction 使用 string + JsonElement，缺少统一的 typed action；
- 普通动作日志没有 legalActions。

如果另写一套 AI 枚举逻辑，规则更新后必然出现“枚举允许但执行拒绝”或“真人能做但 AI mask 不开放”的漂移。

### 5.2 建议 API 与数据结构

第一步把 wire JSON 与规则命令分离：

~~~csharp
public sealed record GameActionCommand(
    string Family,
    IReadOnlyDictionary<string, object?> Parameters);

public sealed record LegalActionSet(
    string ActionSpaceVersion,
    int Actor,
    IReadOnlyList<LegalAction> Actions,
    LegalActionMask Mask,
    string LegalSetHash);

public sealed record LegalAction(
    string ActionId,
    string Family,
    IReadOnlyDictionary<string, object?> Parameters,
    SelectionConstraint? Selection = null);

public sealed record ValidationResult(
    bool Ok,
    string Code,
    string? Reason);
~~~

核心入口：

~~~csharp
LegalActionSet Enumerate(GameState state, int actor, ActionPurpose purpose);
ValidationResult Validate(GameState state, int actor, GameActionCommand command);
ValidationResult ExplainInvalid(GameState state, int actor, GameActionCommand command);
ApplyResult ApplyValidated(GameEngine engine, int actor, GameActionCommand command);
~~~

ActionPurpose 至少区分：

- GameplayPolicy：AI 可学习的卡牌策略；
- FullProtocol：真人协议可执行动作，包含投降／平局；
- Debug：仅 GM／测试；
- Audit：历史兼容动作。

PlayerActivity、RequestTurnExtension 等房间时钟控制不属于 GameEngine 状态动作，保持在 GameRoomManager，但同样应有独立 typed validator；不得混进模型 action head。

### 5.3 LegalAction 简洁 JSON 示例

~~~json
{
  "actionId": "attack:self_c3:opp_leader",
  "family": "Attack",
  "params": {
    "sourceId": "self_c3",
    "targetId": "opp_leader",
    "targetIsLeader": true
  },
  "maskPath": {
    "family": 4,
    "source": 2,
    "target": 0
  },
  "ruleTrace": ["TURN_OWNER", "PHASE_MAIN", "SOURCE_ACTIVE", "TARGET_LEGAL"]
}
~~~

ruleTrace 只用于调试数据／ExplainInvalid，线上 policy 输入不需要中文 reason。

### 5.4 分层动作空间

| 决策域 | family | 参数／约束 |
|---|---|---|
| 开局 | ChooseFirstPlayer | goFirst=true／false |
| 调度 | Mulligan | redraw=true／false |
| 主要阶段 | PlayCard | hand instance；满角色区时 overflow target |
| 主要阶段 | AttachDon | target instance；count=1..activeDon |
| 主要阶段 | UseEffect | leader／character／stage source instance |
| 主要阶段 | Attack | attacker instance；leader／character target instance |
| 主要阶段 | EndTurn | 无参数 |
| 阻挡 | DeclareBlocker | blocker instance |
| 阻挡 | PassBlock | 无参数 |
| 反击 | PlayCounter | hand instance；counter icon／event mode |
| 反击 | PassCounter | 无参数 |
| Prompt | PromptResponse | promptId；候选序列；min/max／是否有序／stop mask |
| 协议 | Surrender | 无参数，默认不训练 |
| 协议 | RequestDraw／RespondDraw | 描述或 accept，默认不训练 |

实例动作必须用稳定 card instance token，而不是可随手牌删除而变化的 handIndex。现有 wire 协议可由 adapter 在当前状态把 instance token 转为 handIndex；未来网络协议也可逐步改成实例 ID。

### 5.5 参数化动作、Prompt 和 mask

简单动作可完全展开为 LegalAction 列表。Prompt 不应盲目展开全部组合：从 N 个候选中选 k 个会组合爆炸，排序选择还会产生排列爆炸。建议 mask 分层：

1. familyMask：当前允许哪些动作族；
2. sourceMask：允许哪些手牌／场上来源；
3. targetMask：给定 family、source 后允许哪些目标；
4. amountMask：AttachDon 等允许的数值；
5. promptChoiceMask：每一选择位置可选的候选；
6. stopMask：达到 min 后是否可以结束，达到 max 后必须结束；
7. noRepeatMask：已选候选后屏蔽重复；
8. orderSensitive：排序 prompt 保留选择顺序，普通集合 prompt 规范化排序。

PromptResponse 可表示成带 SelectionConstraint 的参数化 LegalAction：

~~~json
{
  "actionId": "prompt:p17",
  "family": "PromptResponse",
  "params": {"promptId": "p17"},
  "selection": {
    "candidateIds": ["self_h1", "self_h4", "choice_yes"],
    "minChoose": 1,
    "maxChoose": 2,
    "allowRepeat": false,
    "orderSensitive": false
  }
}
~~~

模型必须逐层采样，每层先应用 mask。最终具体命令在进入队列前再次调用 Validate；若状态 token 已过期则丢弃并重新决策，不允许拿旧 mask 落子。

### 5.6 与 HandleAction 共用规则真源

推荐渐进式重构：

1. 为每个 family 建立 ActionRuleDescriptor，包含 Parse、EnumerateCandidates、Validate、Apply；
2. 先把 GameEngine handler 中纯检查搬到 Validate，Apply 只接收已验证 typed command；
3. EnumerateCandidates 生成有限候选，并逐个调用同一 Validate 过滤；
4. HandleAction 仅做 wire parse、调用 descriptor、记录 accepted／rejected 和启动执行；
5. StateSnapshotBuilder 的按钮状态、P0-A、AI runtime、BotDriver 都改调 Enumerate；
6. 删除重复 if 前，使用双跑监控比较旧／新校验结果。

对动作候选很多但规则昂贵的场景，可先按静态结构生成候选，再用 Validate 过滤。**Validate 是最终真源**，不能为了枚举性能复制规则判断。

### 5.7 确定性、缓存与性能

- 所有 source／target 先按区域枚举，再按实例 ID token 稳定排序；
- LegalSetHash 对 actionSpaceVersion + actor + canonical actions／constraints 计算；
- 缓存键不能只用 Tick，因为 prompt 在异步续程中可能变化；至少用规则相关 stateVersion／canonical digest + actor + purpose；
- 任一状态变更、pending prompt 变化、规则集变化立即失效；
- 第一版目标为单次枚举 p95 不高于 5 ms、p99 不高于 10 ms（在正式硬件与代表性复杂场面测量后冻结），并报告各动作域；
- 批量离线导出可多进程按对局并行，单局内部保持顺序；
- 不缓存含真实账号或隐藏 observation，只缓存规则候选／mask。

性能目标不能牺牲正确性。如果某个脚本效果需要执行才知道能否支付成本，应把“可支付性”抽为无副作用 predicate；不能让 Enumerate 试执行并回滚共享 GameState。

### 5.8 ExplainInvalid

ExplainInvalid 返回稳定错误 code 和可选中文原因，例如：

- NOT_ACTOR_TURN
- WRONG_PHASE
- PENDING_PROMPT
- SOURCE_NOT_FOUND
- SOURCE_TAPPED
- INSUFFICIENT_DON
- TARGET_NOT_LEGAL
- CHOICE_COUNT
- CHOICE_DUPLICATE
- CHOICE_NOT_ALLOWED
- STALE_STATE_TOKEN

错误 code 供测试、日志和监控聚合；中文 reason 继续服务 MsgActionRejected。不得把隐藏信息写入 reason，例如不能通过“对手没有反击牌”解释自动跳过。

### 5.9 属性测试、变形测试与退出门槛

必需测试：

- **Soundness**：枚举出的每个具体动作都通过 Validate；
- **Completeness**：所有历史 accepted 动作规范化后均在当时 LegalActionSet 中；
- **Execution**：对克隆／fixture 状态应用枚举动作，不出现 MsgActionRejected；
- **Mutation**：越界 ID、重复选项、错误 actor、错误 phase、负数／超量 count 均被拒绝；
- **Prompt property**：任意 validChoices、min、max 下逐步 mask 永远只生成合法长度、无重复响应；
- **ID permutation**：仅重命名实例 ID 后，动作语义集合等价；
- **seat mirror**：交换 P0／P1 与视角后，合法动作对称；
- **irrelevant ordering**：无序集合内部插入顺序变化不改变 canonical legalSetHash；
- **version differential**：规则集未改变相关卡时结果一致，目标热更卡只在预期状态变化；
- **fuzz**：随机合法状态／动作序列持续执行，mask 后动作零拒绝。

P0-B 退出门槛：

- 单元、属性、变形 corpus 中枚举动作 Validate 通过率 100%；
- 至少 100,000 个引擎决策点由 mask 采样后，进入同版本 HandleAction 的合法率 100%；
- P0-A 所有被纳入数据集的 actionTaken 合法覆盖率 100%；任何缺失样本隔离；
- 最新版本历史 accepted 覆盖率至少 99.99%，剩余差异必须有明确 adapter／bug 分类，不能静默；
- 双跑期间枚举／执行规则漂移为 0；
- actionSpaceVersion、canonical 排序和 legalSetHash 重跑一致。

## 6. 两个 P0 的依赖关系与可并行边界

~~~text
版本／artifact 注册表
  ├─→ P0-A 日志解析、历史 adapter、重放骨架
  └─→ P0-B 当前版本 typed action、统一 Validate／Enumerate
          ↓
P0-A 在每个动作前调用 P0-B
          ↓
training_sample.v2 + 合法 mask + 一致性／隐私报告
          ↓
P1 数据集与模型训练
~~~

必须先行：

- 定义版本注册表、training_sample.v2、actionSpaceVersion 和 canonical digest；
- 选出覆盖开局、prompt、战斗、终局的 golden logs；
- 明确历史 adapter 的支持版本。

可并行：

- P0-A 可先完成日志 parser、accepted 磁带、artifact worker、断点与 manifest；
- P0-B 可按开局／主阶段／战斗／prompt 动作域分步迁移，但同一时刻只能有一个最终 Validate 真源；
- 数据治理可并行准备 HMAC、去重、split manifest 和隐私扫描；
- 模型团队可用合成 schema 写 dataloader，但不得用无验证真实样本做晋级训练。

最终合流：

- 重放器在**动作执行前**调用枚举器；
- 历史 actionTaken 必须在 legal set；
- 同一 typed action 再由同源 Validate／Apply 执行；
- 两边共同通过 corpus 门槛后，才解除训练阻塞。

## 7. 数据治理与数据集构建

### 7.1 敏感数据处理

原始日志包含账号、完整卡组、隐藏牌序、prompt 和完整动作，只能留在受控内部数据区。

处理顺序：

1. 在受控 staging 读取原始日志；
2. 规范化账号后使用 HMAC-SHA256(datasetSecret, normalizedAccount) 生成 playerGroupKey；
3. datasetSecret 存在密钥管理中，不进入代码、manifest 或训练节点；manifest 只记 keyId；
4. 删除 accountName、displayName、sessionId、requestId、聊天、IP／设备字段；
5. matchId 也转换为 dataset 内 token；
6. 原始 card instance GUID 转为单局、单视角 token；
7. policy observation 经过字段白名单构建，而不是对完整对象做黑名单删除；
8. 输出后运行结构扫描、已知账号词典扫描、完整卡组／隐藏区模式扫描；
9. 训练节点只获得脱敏 shard，不挂载原始 MatchLogs。

### 7.2 去重和过滤

纳入行为克隆的基本条件：

- schema、seq、matchId、尾行结构合法；
- 有可信 match_start，版本 artifact 可用；
- 真人模式，排除 MatchKind.Bot、GM、Debug 和 coverage runner；
- 动作磁带可完整重放，终局或明确允许的完整截断窗口；
- actionTaken 在合法集合；
- observation 隐私检查通过。

默认过滤／分类：

| 数据 | 行为克隆 policy | value／胜负目标 | 用途 |
|---|---|---|---|
| accepted 真人动作 | 纳入 | 视终局质量 | 核心标签 |
| rejected 请求 | 不纳入 | 不纳入 | QC、安全和 UI 错误分析 |
| 系统自动动作 | 不作真人标签 | 仅推进重放 | 重放磁带 |
| 正常完赛 | 纳入 | 纳入 | 高质量 |
| 早期投降 | 可降权，阈值预注册 | 谨慎／降权 | 避免结果噪声 |
| 操作／断线超时 | 前序动作可用于 policy | 默认排除或低权重 | 结果受非策略因素影响 |
| Bug 平局 | 动作可低权重 | actorScore=0.5 仅分析，第一版 value 可排除 | 异常版本监控 |
| 无 match_end | 默认排除 | 排除 | 可做无结果 BC 需单独标识 |
| UnknownHuman | 确认非 Bot 后才纳入 | 同左 | 历史兼容 |

### 7.3 玩家水平和质量加权

不要只学高手，也不要让大量弱手动作淹没策略：

- 使用对局时点的 rating_before／rank bucket，不用当前排名或赛后信息；
- rank 联结失败时标 unknown，不用显示名猜；
- policy 样本权重可由质量、玩家水平、版本新鲜度、终局类型组合；
- 高手样本适度增权但设置上限，例如最终权重限制在 0.5～2.0，具体值由验证集选择；
- 每局权重归一，避免长局贡献远多于短局；
- 对热门领袖／动作族做分层采样或逆频率温和加权；
- 保留普通和较弱玩家覆盖目标总体，另建 expert-only 消融对比；
- 同时报告未加权行为克隆与加权模型，防止“高手权重”破坏大众分布。

### 7.4 按玩家 + 时间 + 版本切分

严禁随机按样本行切分。建议：

1. 先按 rulesVersion + cardDbContentHash 建版本桶；
2. 用 HMAC playerGroupKey 把账号确定性分配到 train／validation／test cohort；
3. 为保证玩家不跨集合，只保留双方都属于同一 cohort 的对局；跨 cohort 对局不进入监督 split，可留作受控分析；
4. test cohort 使用最新冻结时间窗，validation 使用更早的相邻窗口，train 只用 cutoff 之前；
5. 同一 match 的双方样本必须在同一 split；
6. 同一玩家的所有版本样本默认留在同一大 split，另做“新版本迁移”专用 temporal suite；
7. 检查 playerGroupKey、matchToken、源文件 hash 在 split 间交集为 0。

如果同 cohort 保留率过低，可采用玩家图连通分量切分，但必须先测是否出现巨型连通分量；不得为了多保留数据而允许同一账号出现在 train 和 test。

### 7.5 数据 lineage 与发布

每次数据集发布需要：

- dataset card，描述来源、截止点、允许用途、隐私边界；
- per-version replay coverage；
- 过滤和隔离原因直方图；
- split 账号／对局／决策数；
- 领袖、段位、先后手、matchKind、动作族分布；
- source manifest → exporter → shard → model 的完整哈希链；
- 可撤销列表：发现某源日志或账号需删除时，能定位所有受影响 shard 和模型。

## 8. 模型路线

### 8.1 第一条可交付路线

1. **Prompt 候选排序模型**：validChoices 天然存在，先解决 Option、LifeTrigger、卡牌选择、排序和成本选择；但仍需 P0-A 重建候选卡当时特征，并需 P0-B 提供 selection mask。
2. **结构化行为克隆 policy**：覆盖主要阶段、攻击、阻挡、反击和 Prompt，使用分层 action head 与硬 mask。
3. **可选 value head**：在 policy 稳定后预测当前 actor 最终得分，辅助排序和搜索。
4. **可选搜索**：短视确定信息可做有限 lookahead；隐藏信息场景优先 ISMCTS／belief sampling，而不是让模型读取真实 hidden state。
5. **可选自博弈 RL**：只有行为克隆、评测、版本和服务基础稳定后再做，用冻结历史模型池抑制遗忘和漏洞共谋。

第一版不建议直接上复杂 RL。原因是环境／动作空间尚未稳定、reward 容易被投降／超时／漏洞污染，且当前最紧缺的是正确数据，不是更复杂优化算法。

### 8.2 推荐模型结构

- **卡牌编码**：cardNumber embedding + 类型／颜色／费用／力量／反击／公开关键词和当前修正；
- **区域编码**：self hand、self field、trash、life public、opponent public field 等 zone embedding；
- **顺序编码**：仅对 actor 确实知道顺序的区域启用；未知牌库不得编码真实顺序；
- **全局状态**：turn、phase、first player、Don、life／hand／deck counts、battle、prompt；
- **集合编码**：Transformer／Set Transformer／attention pooling 处理变长卡牌集合；
- **动作评分**：状态向量与 LegalAction 候选交叉注意力或双塔打分；
- **分层 head**：family → source → target／amount → prompt choices；
- **硬 mask**：每层 softmax 前把非法项置为负无穷；
- **value head**：共享 encoder 后预测 actorScore，可在第二阶段加入。

模型规模优先从约 10M～100M 参数的结构化网络试验，以验证数据和动作接口。不要用通用 LLM 直接输出 JSON 动作：它更慢、更难保证合法、难利用实例级 mask，也容易把文字关联当成规则。LLM 可用于离线日志解释或开发辅助，不作为权威落子器。

## 9. 训练目标、损失与防泄漏

### 9.1 Policy loss

分层 policy loss：

~~~text
L_policy =
  L_family
  + λ_source L_source
  + λ_target L_target
  + λ_amount L_amount
  + λ_prompt L_prompt_sequence
~~~

- 所有 logits 先应用 legal mask；
- Prompt 排序使用 autoregressive pointer／listwise loss；
- min/max 选择通过 stop token mask 表达；
- 对多个语义等价动作，可用集合目标或 soft target，避免把唯一真人选择当作唯一正确答案；
- 每局和每动作域做权重归一。

### 9.2 Value loss

- 正常胜／负目标为 1／0；
- 平局可为 0.5，但第一版可把 Bug 平局从 value 训练排除；
- 使用 binary cross entropy 或 Huber／MSE 对 actorScore；
- 对投降、超时、断线结局使用单独质量权重；
- value 只用动作前可见 observation，结果只作为 label。

### 9.3 样本权重和不平衡

总权重可由以下乘积并截断：

~~~text
replayQuality × terminalQuality × skillWeight × leaderBalance × actionFamilyBalance × perMatchNormalization
~~~

应做的消融：

- 无技能加权 vs 温和高手加权；
- 全部完赛 vs 排除超时／早投；
- 热门领袖自然分布 vs 分层采样；
- 最近版本 vs 多版本预训练后微调。

EndTurn、PassBlock、PassCounter 等高频动作可能让 top-1 虚高。采用分层 head、类别平衡和 per-family 指标，不要只报总体准确率。focal loss 只在验证证明有益时使用，避免破坏概率校准。

### 9.4 校准

- 在完全隔离的 validation 上做 temperature scaling；
- 报 ECE、Brier score、top-1 margin 和 entropy；
- 低置信状态可触发有限搜索或安全基线，而不是放宽 mask；
- 温度、采样 top-p 等推理参数进入模型注册表。

### 9.5 隐藏信息泄漏检查

至少包含：

- schema 白名单测试；
- 对手手牌／生命暗牌／牌库顺序 canary；
- 交换对手隐藏状态但保持公开 observation 不变，模型 logits 应逐字节／容差内不变；
- 删除未来事件后样本不变；
- rank 使用 rating_before，不含 rating_after；
- train／test 玩家、match、文件 hash 零交集；
- 模型服务请求 payload 抓包检查，不含完整 GameState。

## 10. 评测体系

### 10.1 离线评测

必须报告：

- family、完整动作和 prompt 的 top-1／top-3／top-k；
- NLL、perplexity、ECE、Brier；
- mask 前候选错误率、mask 后合法率；
- actionTaken 在 legal set 的覆盖率；
- 按领袖、先后手、turn、phase、段位、动作族、版本分层；
- ID permutation、seat mirror、无关顺序变形的一致性；
- 长尾卡牌／新卡组 OOD suite；
- 推理 p50／p95／p99 与 batch 性能。

模仿准确率只表示像历史玩家，不表示胜率。多个动作都可能合理，必须结合引擎对局。

### 10.2 引擎对局

基线：

- 当前 BotDriver 最小推进基线；
- 明确规则的脚本 bot；
- 上一已冻结模型；
- 不同训练消融模型；
- 同模型自博弈；
- 后续可选搜索／value 版本。

协议：

- 固定合法 deck pair 和 seed，交换先后手做镜像成对对局；
- 每个领袖／卡组组合有最低局数；
- 同一模型对比使用相同 seed bank，降低随机方差；
- 记录所有 action、legalSetHash、模型 logits 摘要、延迟和 fallback；
- 任何非法动作、卡死、prompt 未响应、状态 token 过期误执行都为工程失败；
- 专设漏洞／无限循环／平局拖延／超时对抗集。

### 10.3 真人影子、封测和线上阶梯

1. **影子**：真人照常下棋，模型只在每个稳定决策点预测，不影响对局；测动作覆盖、延迟和隐藏 payload。
2. **内部封测**：知情测试者，固定模型／卡组，集中找卡死、长尾和明显漏洞。
3. **受邀分层封测**：按段位、领袖和设备分层，模型版本冻结；先验证工程和大致胜率。
4. **小流量真人阶梯**：服务端灰度、可一键回退，AI 明确标识；检查分层、推理容量和投诉。
5. **正式统计阶段**：重新冻结版本，按第 2 节预注册口径收集去重账号样本。

### 10.4 反作弊与追踪

- 模型不读取账号身份做动作，不对特定玩家记忆；
- 服务端保留 matchToken、modelId、modelSha256、featureSchema、actionSpaceVersion、legalSetHash、inferenceSeed；
- 记录 shadow／live、fallback 原因、延迟、过期决策丢弃；
- 模型和玩家都通过权威引擎校验；
- 评测账号抽样和结果分析脚本版本化；
- 禁止人工挑选“好看的”对局进入主指标。

## 11. 工程架构与推理接入

### 11.1 离线训练架构

~~~text
受控原始日志／备份
  → 去重清单 + 版本解析
  → 按 artifact 启动隔离 replay worker
  → P0-A 重放
  → P0-B 动作前 legal set
  → 脱敏 training_sample.v2
  → Parquet／Arrow + Zstd 分片
  → 数据质量／隐私／split 门禁
  → 训练
  → 离线／引擎评测
  → 模型注册表
~~~

JSONL 可作为审计和小 fixture，正式数百万样本建议转换为列式压缩分片，减少重复字段和训练 I/O。

### 11.2 服务端推理

沿用 BotDriver 的正确边界：AI 最终动作必须通过 GameRoomManager.EnqueueBotAction 回到房间串行队列。建议新增 AiBotDriver／AiDecisionCoordinator：

1. 在引擎达到稳定决策点时取得 actor observation 和 LegalActionSet；
2. 生成 decisionToken = roomId + tick／stateVersion + promptId + actor + legalSetHash；
3. 异步调用推理服务，不在房间单读者队列中等待网络；
4. 返回后重新进入房间队列；
5. 核对 decisionToken 和当前决策者；已过期则丢弃并重新决策；
6. 对返回具体动作再次 Validate；
7. 通过 EnqueueBotAction 执行并记录模型审计信息。

该异步返回与房间状态 token 涉及并发时序，进入实现时应按项目规则升级关键档审查；本方案不在当前任务中修改该链路。

### 11.3 超时与兜底

初始建议目标，需以测试服容量实测冻结：

- 模型本体 p95 不高于 200 ms，p99 不高于 400 ms；
- 端到端硬超时 750 ms；
- micro-batch 等待不超过 5～10 ms；
- 容量按实测峰值决策 QPS 的至少 2 倍规划。

超时后只能从当前 LegalActionSet 选择确定性安全基线：

- Prompt：满足 min/max 的合法默认；
- Block／Counter：合法 Pass；
- 主阶段：优先经过规则验证的脚本动作，否则 EndTurn；
- 开局／调度：冻结的默认策略。

若 legal set 为空但引擎认为需要 actor 操作，这是 P0-B 严重故障，应停止该模型房间并告警，不得发送猜测动作。连续推理故障可把整局切换到脚本基线，但必须记录，且真人主指标中按预注册规则计入 AI 工程失败。

### 11.4 模型注册表、灰度和回滚

每个模型记录：

- modelId、权重 SHA-256、训练代码 commit；
- datasetId 和 source manifest；
- featureSchema、actionSpaceVersion；
- engine／rules／cardDb 兼容范围；
- 推理运行时、温度、随机 seed 策略；
- 离线、引擎、影子、真人评测报告；
- 当前状态：candidate／shadow／canary／active／retired。

灰度按 modelId 路由，不覆盖旧权重。回滚只需恢复上一个已验证 modelId，不改变进行中房间锁定的模型，除非严重安全兜底明确要求整局切脚本。

### 11.5 安全边界

- 绝不把模型放在客户端或把完整状态发给客户端推理；
- 推理服务只接 actor 可见 observation + legal mask；
- 原始日志、HMAC key、完整卡组与隐藏状态不进入模型服务；
- 服务端记录落子，客户端只收到正常 MsgGameState；
- 模型服务无权直接写数据库、匹配或排位结算。

## 12. 分阶段路线图与粗略工作量

人周为总投入，不是日历承诺；规则版本遗失、历史 adapter 数量和枚举迁移复杂度会显著改变范围。

| 阶段 | 工作包 | 依赖 | 退出门槛／产物 | 总投入人周 | 单人日历参考 | 2～3 人小团队日历参考 |
|---|---|---|---|---:|---:|---:|
| P0-0 基线冻结 | 版本清单、golden logs、schema、action space、隐私边界、指标预注册 | 无 | registry v1、fixture、决策记录 | 1～2 | 1～2 周 | 1 周 |
| **P0-A 重放导出器** | artifact worker、事件 adapter、accepted tape、动作前 observation、一致性、断点、manifest | P0-0 | supported 版本 golden 100%，最新版本 ≥99.5% 重放 | 4～7 | 4～7 周 | 3～5 周 |
| **P0-B 合法枚举／mask** | typed actions、统一 Validate／Enumerate、prompt constraints、HandleAction 合流、属性测试 | P0-0 | 100k 决策零非法，历史动作覆盖门槛 | 5～8 | 5～8 周 | 3～6 周 |
| P0-C 合流与数据发布 | P0-A 调 P0-B、脱敏、去重、split、Parquet、隐私扫描 | P0-A+B | dataset v1、data card、lineage、No-Go 清零 | 3～5 | 3～5 周 | 2～3 周 |
| P1 Prompt 模型 | dataloader、候选编码、listwise／pointer、校准、离线评测 | P0-C | Prompt 模型与分层报告 | 2～4 | 2～4 周 | 1～3 周 |
| P2 结构化 BC | 状态 encoder、分层 action scorer、value 可选、引擎 tournament | P0-C，可复用 P1 | 候选模型击败基线且零非法 | 5～9 | 5～9 周 | 3～6 周 |
| P3 推理与真人验证 | 模型服务、状态 token、超时兜底、审计、影子、封测、灰度、预注册统计 | P2 | ≥1,000 有效局、≥300 去重账号，并扩样至聚类／Wilson 双下界 ≥60% | 5～9 + 收集期 | 5～9 周 + 收集 | 3～6 周 + 收集 |
| P4 可选增强 | value 搜索、ISMCTS、模型池、自博弈 RL | P3 瓶颈证据 | 明确优于冻结 BC，且无新漏洞 | 6～12／轮 | 不预承诺 | 不预承诺 |

到具备严格真人 60% 验证能力的基础投入约 25～44 人周，不包含为了提高棋力可能发生的额外模型／规则迭代和真人样本收集等待。经验性日历参考：

- 单人全栈：约 7～12 个月，且并行度低、关键人风险高；
- 2～3 人小团队：约 14～24 周到可启动正式真人统计；
- 若首个 BC 未达目标，每轮数据／模型／评测迭代另预留约 3～8 人周。

这不是交付承诺。P0 发现旧 artifact 大量缺失或动作规则无法纯校验时，应重新估算。

## 13. 角色与资源

### 13.1 人员建议

单人可做，但应严格串行：

1. 版本／重放；
2. 动作规则；
3. 数据治理；
4. 模型；
5. 服务与真人评测。

2～3 人更合理：

- **引擎／重放负责人**：P0-A、artifact、历史 adapter、一致性；
- **规则／服务负责人**：P0-B、HandleAction 合流、推理接入与兜底；
- **数据／ML 负责人**：治理、split、模型、评测和模型注册表。

数据隐私、真人实验口径和上线灰度需要代码 owner／产品共同评审。涉及异步推理回房间队列、服务恢复、排位影响时，按关键档处理。

### 13.2 CPU、GPU、内存与存储数量级

建议从以下数量级起步，再按 benchmark 调整：

- **重放 CPU**：16～32 个现代 CPU 核；按对局多进程并行；
- **内存**：64～128 GB，避免大量并行 GameEngine 和 shard builder 抖动；
- **训练 GPU**：原型 1 张 16～48 GB 显存 GPU；需要快速迭代时 2～4 张同级 GPU，不预设必须大集群；
- **推理 GPU／CPU**：先在测试服测结构化模型单请求和 batch；按峰值决策 QPS × 2 容量，而不是按在线连接数猜；
- **工作存储**：原始备份目前为 GB 级，但数百万条重复 observation 解压后可能达到数十至数百 GB；为原始、恢复、columnar、索引、模型和两份安全余量准备约 0.2～1 TB 可用空间是合理起点；
- **临时产物**：遵守仓库规范，只使用 E:\GrandUMI-Temp\，不回退 C 盘。

近 8 万局按抽样可能生成约 850 万 accepted 动作，但最终样本数、压缩率和 GPU 时间必须在 P0-C 实际导出后重新测量，不能据估算编造精确成本。

## 14. 风险登记表

| 风险 | 早期信号 | 缓解措施 | 触发停止条件 |
|---|---|---|---|
| 旧规则版本不可复现 | ruleset 包／engine artifact 缺失、checkpoint 分歧 | 不可变 registry、归档二进制／卡表／插件、版本隔离 | 目标版本 artifact 缺失或 golden 任一分歧，停止该版本导出 |
| cardDbVersion 无内容身份 | 同 ID 下卡表变化 | canonical SHA-256、归档文件 | hash 不符立即停止，不用 local-card-json 兜底 |
| RNG／ID 漂移 | randomSeq、卡实例、洗牌不一致 | 固定算法版本和运行时，random event 校验 | 任一 admitted 对局随机事件不一致 |
| accepted 配对错误 | requested／accepted 不守恒、data 缺失 | 版本化序列 adapter、未来 accepted 自带 data | 无法唯一配对即隔离整局 |
| 动作空间漂移 | 枚举动作被 HandleAction 拒绝 | 同一 descriptor 真源、双跑、属性测试 | mask 后出现 1 个非法执行即停止 rollout |
| Prompt 组合爆炸 | 全排列枚举过大 | selection constraint、逐步 mask、stop token | 枚举内存／延迟超预算且无法约束化 |
| 日志偏差 | 热门领袖、高频玩家、长局占比过高 | 账号等权、per-match 归一、分层采样 | 目标总体与训练／评测分布无法描述 |
| 高手样本不足 | 高段位 bucket CI 很宽 | as-of rank 联结、温和增权、定向收集 | 不得宣称高段位 60%，总体仍可继续 |
| 卡组／领袖长尾 | OOD 合法率或胜率骤降 | 长尾 suite、card embedding、补数 | 目标格式核心领袖无最小覆盖，不进入全面灰度 |
| 隐藏信息泄漏 | 对手暗牌变化引起 logits 变化 | 白名单 builder、canary、服务抓包 | 任一隐私 canary 失败，停止数据发布和模型服务 |
| 训练／评测泄漏 | player／match hash 跨 split | HMAC cohort + 时间切分 + 交集检查 | 任一主键交集非 0，作废评测 |
| 段位未来泄漏 | 使用当前／赛后 rating | matchId + rating_before as-of join | 无法证明时点则 rank 标 unknown |
| 服务延迟／容量 | p99、fallback 率上升 | micro-batch、容量 ×2、脚本兜底、灰度 | 超时／fallback 超出预注册 SLO，暂停扩流 |
| 状态过期落子 | 异步推理返回旧 tick／prompt | decisionToken、回队列重验 | 发生旧状态动作执行，停止 AI 接入 |
| 模型钻漏洞／拖平 | 异常循环、平局／超时激增 | adversarial suite、规则修复、动作上限、模型池 | 相对冻结基线异常率显著上升，暂停真人流量 |
| 环境更新 | 新卡、热更、核心发布后性能突变 | 兼容矩阵、模型／规则按局锁定、重新 shadow | 未注册版本不得调用 active 模型 |
| 目标定义错误 | 高频玩家重复、只挑弱段位、排除平局 | 预注册、账号等权、严格胜利口径 | 口径在看结果后改变，旧结果不得用于 60% 声明 |
| 行为克隆上限 | 离线像人但引擎胜率停滞 | value、有限搜索、expert weighting、后续自博弈 | 先做诊断，不直接跳复杂 RL |

## 15. “现在就做”的前 10 个工程 issue

1. **P0-A：建立确定性重放训练导出器骨架**  
   新建 artifact registry、MatchLogEventAdapter、AcceptedActionTapeBuilder 和按局 quarantine；用 10～20 个 golden logs 跑通 seed + deckRaw + accepted／系统动作。

2. **P0-B：定义 typed action 与统一 LegalActionSet／mask API**  
   冻结 grandumi.action.v1、actionId canonical 规则、LegalAction／SelectionConstraint／ExplainInvalid，并先接 ChooseFirstPlayer、Mulligan、EndTurn 和 PromptResponse。

3. **补齐 match_start 可重放版本字段**  
   增加 engineArtifactId、cardDbContentHash、rulesetManifestHash、RNG／ID／opening protocol 版本和 AlwaysPrompt 配置；保持 matchlog.v1 兼容或明确升级 schema。

4. **让 player_action_accepted 自包含规范化 data**  
   增加 request correlation、source=player／system 和稳定实例语义；为旧日志编写 requested→accepted adapter 测试。

5. **实现 TrainingObservationBuilder 白名单**  
   基于 actor 视角输出 training observation，删除账号／展示字段；增加对手手牌、生命暗牌、牌库顺序和 replayHands canary。

6. **迁移主要阶段动作到统一规则 descriptor**  
   覆盖 PlayCard（含满场腾位）、AttachDon、UseEffect、Attack、EndTurn；StateSnapshotBuilder 和 HandleAction 双跑比较。

7. **迁移战斗与 Prompt 动作并做属性测试**  
   覆盖 Block、Counter、多次反击、LifeTrigger、Option、排序和 min/max；完成 100,000 决策零非法测试。

8. **实现 ReplayConsistencyChecker**  
   对齐 random_event、prompt、public／private canonical digest、终局；生成 per-version coverage 和 quarantine 报告。

9. **实现 HMAC、重复备份去重、玩家／时间／版本 split manifest**  
   读取受控内部存储中的新快照清单但不改备份本体；跨 split 玩家、match、文件 hash 交集必须为 0。

10. **发布第一份 training_sample.v2 数据质量报告**  
    输出动作域、领袖、段位、先后手、版本、过滤原因、隐私扫描和规模；只有 P0-A+B 退出门槛通过才允许启动 Prompt／BC 正式训练。

## 16. 明确不做清单

第一版明确不做：

- 不把当前空 observation 导出当训练数据；
- 不把业务统计中的排行榜对局数冒充已备份原始日志数；
- 不依赖生产常开 private_snapshot 解决训练；
- 不把完整 GameState／hiddenState 发给模型；
- 不让客户端运行权威 AI 或接触隐藏状态；
- 不维护第二套“AI 合法规则”；
- 不把 rejected 请求当真人策略动作；
- 不按样本行随机切 train／test；
- 不用当前段位、赛后 rating 或未来动作作为特征；
- 不用 LLM 直接生成未 mask 的 JSON 动作；
- 不在第一版直接建设复杂自博弈 RL、分布式 RL 平台或大规模 MCTS；
- 不为追求覆盖率静默迁移旧规则或跳过重放分歧；
- 不在没有影子／封测证据时直接进入排位或大流量；
- 不让 AI 对局影响真人排位结算，除非未来另有明确产品设计和关键档审查；
- 不承诺固定日期、固定训练成本或一次训练必达 60%。

## 17. Go／No-Go 决策门

### Gate A：日志可用

Go：

- 源 manifest、备份截止点、重复去重和敏感权限明确；
- 目标版本 artifact 齐备；
- 日志结构和终局过滤可解释。

No-Go：

- 把业务统计局数直接当原始日志；
- 源文件冲突或版本身份不明；
- 原始敏感日志直接进入训练节点。

### Gate B：P0-A 重放

Go：

- supported golden 100% 一致；
- 最新版本 ≥99.5%、目标整体 ≥95% 可重放；
- 任一分歧局零样本；
- 幂等重跑哈希一致。

No-Go：

- 缺 engine／rules／cardDb artifact；
- prompt、random、终局或 checkpoint 任一静默分歧；
- 仍依赖空 private_snapshot。

### Gate C：P0-B 合法动作

Go：

- 枚举、Validate、HandleAction 同源；
- 100,000 决策 mask 后合法率 100%；
- 纳入数据的 actionTaken 合法覆盖 100%；
- Prompt 组合约束和 actionSpaceVersion 冻结。

No-Go：

- 出现枚举合法但执行拒绝；
- 实际 accepted 不在合法集合且无明确历史 adapter；
- 模型需要自行猜 handIndex 或非法目标。

### Gate D：数据与模型

Go：

- 隐私 canary、split 零交集、data card、lineage 通过；
- 离线分层优于脚本／简单基线；
- 引擎对局零非法、零卡死，并稳定优于上一模型。

No-Go：

- 发现隐藏信息、未来信息或评测玩家泄漏；
- 只有总体 top-1 提升，长尾和引擎胜率退化；
- 模型服务 payload 含完整状态。

### Gate E：真人灰度

Go：

- 影子延迟、过期决策、fallback 和容量达标；
- 内部／受邀封测无规则漏洞或异常拖平；
- 模型、卡组、格式、玩家总体和统计计划已预注册；
- 可一键回滚。

No-Go：

- 非法落子、旧状态落子、泄漏、卡死任一发生；
- 推理超时／fallback 超 SLO；
- 规则或卡牌版本不在兼容矩阵。

### Gate F：60% 达标的唯一正式定义

只有同时满足以下条件，才能写“GrandUMI AI 已达到击败 60% 真人玩家”：

1. 评测模型、推理参数、引擎、rulesVersion、cardDbContentHash、actionSpaceVersion 和 AI 卡组策略全程冻结；
2. 对目标活跃真人总体先按账号规范化去重再随机抽样；账号内对局数有预注册上限，点估计按账号等权；
3. AI 先／后手随机且大致平衡，排除规则预注册；
4. 至少 1,000 场有效真人对局、300 个去重真人账号，并按点估计与设计效应继续扩样；
5. 平局和 AI 工程失败按非胜处理；
6. 同账号重复／镜像配对已折减有效样本量，账号聚类区间与总体严格胜利比例 Wilson 双侧 95% 下置信界均不低于 60%；点估计低于 63% 时至少扩到对应 Wilson 局数门槛；
7. 同时公开总胜／负／平、领袖、先后手、段位、格式、版本、终局原因、延迟和 fallback 分层；
8. 数据、分析脚本和模型 lineage 可复核，账号重复与配对相关性已按预注册方法处理，且无隐藏信息或评测泄漏。

自博弈、BotDriver、脚本基线、内部高手小样本或对局加权胜率即使超过 60%，都不能替代这一定义。

## 18. 最终建议

GrandUMI 已经拥有比一般早期卡牌 AI 项目更有价值的基础：真实大规模对局、可记录的 seed／随机事件、按局锁定的规则集、accepted／rejected 动作、完整 prompt 候选与响应，以及可复用的 MatchReplay。近 8 万局的数量不是主要风险。

正确顺序是：

1. 先把 **P0-A 确定性重放训练导出器** 做到逐局可证明；
2. 同步把 **P0-B 统一合法动作枚举器／mask** 做成引擎唯一规则真源；
3. 两者合流后再发布脱敏、分组切分、带 lineage 的 training_sample.v2；
4. 先 Prompt 排序和结构化行为克隆，证明离线、引擎、影子价值；
5. 只有 BC 确认遇到棋力瓶颈时，再引入 value、ISMCTS 或自博弈；
6. 最终先取得至少 1,000 场有效局、300 个去重账号，再按实际点估计和有效样本量扩样，用账号聚类区间与 Wilson 95% 下界共同验证 60%。

这条路线不会最快产出一个“会动”的模型，但会最快产出一个能够被证明没有重放错位、没有非法动作、没有偷看隐藏牌，并且胜率结论可信的真人对练 AI。
