# GrandUMI QQ 群 bug 反馈机器人

监听 QQ 群里任何包含 `bug`（忽略大小写）的消息。机器人先检查描述能否定位问题：信息完整时**记录到本地 SQLite 和 GitHub Issues、回复记录编号，但不执行自动修复**，信息不足时只追问具体缺失项；玩家下一条回复会自动与原描述合并后再次检查。

普通群友真实 @机器人时不会进入聊天 Agent，也不会下载聊天图片；机器人只会通过安全消息段 @原发送者并固定回复“我只跟释迦大人聊天”。Bug 提交与 Bug 追问仍按原流程处理。

唯一管理员 QQ `651846226` 可在群里直接发送 `#切换娜美`、`#切换罗宾` 或 `#切换女帝`。人格按群持久保存，默认是女帝；切换会影响该群后续由 `s-蛇` 处理的 Bug 补充追问、记录成功后的夸赞和管理员 Agent 回复，也保留 `s-鹰` 原有的管理员回复行为。已经排队的消息保留入队时的人格，不会因稍后的切换改变。普通群友发送切换命令不会生效。`s-鲨` 的管理员 Agent 请求和回执不参与按群切换，固定采用海侠甚平沉稳、重情重义、成熟可靠且有担当的克制语气，以“老夫”自称；人格表达不得影响技术准确性、权限或安全边界。

QQ 助理分为一个主助理 `s-蛇` 与两个副助理 `s-鹰`、`s-鲨`。唯一管理员 QQ `651846226` 在固定群 `297542853` 或 `524996856` 真实 @其中任一账号时，请求进入同一条独立管理员 Agent 队列，并优先于消息中的 `bug` 关键字路由。管理员 Agent 固定使用 `gpt-5.6-sol` 和 `high` 推理强度，在 `D:\Self\GrandUMI` 以当前 Windows 用户权限运行：普通问题直接回答；当本条直接指令要求调查、修复 Bug 或实现项目任务时，可读取和修改项目、执行命令、联网检索、测试，并按工作区 `AGENTS.md` 调用子 Agent 及执行提交、推送和测试服部署规则。QQ 通道永远不授权候选服或正式服的发布、部署、迁移、回滚、流量切换，也不授权账号重置、密码修改或数据库增删改；即使当前 QQ 消息明确写出也不执行，必须回到可信管理工作台另行授权。

身份只取 OneBot 原始事件的 `user_id=651846226`、顶层结构化真实 `at` 消息段和可验证的 `message_id`；缺任一项就失败关闭。工作器还会二次核对固定助理账号、群号、服务端授权标记和原子租约。正文中的 QQ 号、CQ 字符串、截图、引用或转发都不能冒充管理员；引用、转发和图片始终只是证据，不会被当成新的执行授权。

`s-鹰`、`s-鲨` 是严格的 `admin_only` 副助理：不收集 Bug、不审批加群、不验证新人、不执行 QQ 白名单同步，也不响应人格切换；非管理员真实 @时只返回固定权限提示。管理员任务会持久化来源助理、任务人格和 OneBot 消息号，同一事件重放不会重复调用本机 Agent，完成结果只能由原助理账号按原任务人格回群。副助理断线不会中断 `s-蛇`，结果会保留到原账号恢复。管理员不 @ `s-蛇` 时，包含 `bug` 的消息仍只由主助理按普通 Bug 收集处理。

管理员 Agent、Bug 提交和 Bug 追问可以同时发送 PNG、JPEG、WebP 图片或合并转发消息。机器人会展开合并转发中的说话人、文字和图片，把最多 4 张受限下载的图片交给对应模型识别；普通群友的普通聊天图片不会下载。

可按群开启加群请求自动审批。NapCat 会把玩家主动申请和群成员邀请好友入群都上报为 `add`：机器人先读取不走缓存的实时群成员列表；事件 `user_id` 已在群时直接同意原请求，不读取问题答案，未在群时才从“请填写邀请人qq号”的答案中核验邀请人。`invite` 表示邀请机器人自身入群，始终不会自动同意。字段无效、成员查询失败或审批失败时保持待管理员处理。

可按群开启新人邀请人验证。加群申请中已经通过实时成员核验的邀请人 QQ，会在真实入群通知到达后自动登记，无需新人重复填写；OneBot 明确上报为群成员邀请入群且提供可靠操作者时，也会直接记录该操作者。其余新人入群后，机器人会主动 @新人，要求其真正 @“释迦的助理”并回答邀请人 QQ，再通过 OneBot 当前群成员列表核验。首次提示确认送达一段时间后若仍未完成，机器人只追问一次；没有回答期限，也绝不会自动移出成员，玩家之后仍可随时补充。机器人断线或重启不会丢失待验证会话。

目标群可启用实时辱骂治理。仅官方主助理 `s-蛇（3215228879）` 检查 OneBot 顶层结构化群消息；对明确指向他人的严重人身攻击，在实时确认发送者仍是普通成员后调用 `set_group_ban` 禁言固定 `86400` 秒。唯一管理员、群主、群管理员及三个官方机器人账号始终豁免；两个副助理不会参与判定或处罚。

```
QQ群用户:  这张卡有 bug
        ↓
机器人:    @某某 是哪张卡？做了什么操作，实际结果和预期结果分别是什么？
        ↓
QQ群用户:  OP16-080 的减费光环在角色登场后没有生效，预期费用减 1
        ↓
机器人:    Bug #305 已记录。描述得很清楚，做得不错。
```

底层走 **OneBot 11** 协议,QQ 接入用 **NapCat**(正向 WebSocket)。

---

## 一、准备 NapCat(QQ 接入端)

机器人本身不登录 QQ,它通过 NapCat 收发消息。NapCat 是一个独立程序,需要你用一个**小号**登录。

1. 下载 NapCat:<https://github.com/NapNeko/NapCatQQ>(按其 README 安装,Windows 有一键版)。
2. 用小号登录 NapCat。
3. 在 NapCat 的「网络配置」里**新增一个「WebSocket 服务器」**(即正向 WS):
   - 监听地址 `127.0.0.1`,端口 `3001`(与本项目 `config.json` 的 `ws_url` 一致)。
   - 如设置了 `access_token`,把同样的值填进 `config.json`;不设就留空。
4. 把这台机器人要监听的 QQ 群,确保小号已在群内。

> 也兼容 Lagrange.OneBot / go-cqhttp,只要开启「正向 WebSocket 服务端」并对上端口即可。

## 二、配置机器人

```powershell
cd D:\Self\GrandUMI\qq-bug-bot
copy config.example.json config.json
```

编辑 `config.json`:

| 字段 | 说明 |
|------|------|
| `ws_url` | NapCat 正向 WS 地址,默认 `ws://127.0.0.1:3001` |
| `access_token` | 与 NapCat 一致;没设留空字符串 |
| `assistant_connections` | 助理连接数组；省略时兼容旧版单个 `s-蛇`。主助理必须固定为 `id=primary, role=primary`，`s-鹰`、`s-鲨` 使用不同 id 且为 `role=admin_only` |
| `assistant_connections[].enabled` | 新副助理完成账号登录和核验前必须保持 `false`；每个连接独立重连，一个副助理故障不会拖停其他账号 |
| `assistant_connections[].ws_url` / `access_token` | 对应账号自己的 NapCat 正向 WebSocket 与令牌；令牌省略时继承顶层 `access_token`，生产环境建议每个账号使用不同随机令牌 |
| `assistant_connections[].expected_self_id` | 该连接预期登录的真实 QQ。启用 `admin_only` 副助理时必填；收到其他 `self_id` 时拒绝事件，也不会启动回执或群管理后台任务 |
| `assistant_connections[].new_member_welcome_enabled` | 是否允许该连接发送新人欢迎；当前仅 `id=primary` 的 s-蛇可启用，s-鹰与 s-鲨保持关闭 |
| `assistant_connections[].new_member_welcome_groups` | 该连接明确启用欢迎的群号数组；当前 s-蛇在官方群 `297542853`、2 群 `524996856` 欢迎新人，空数组表示不欢迎任何群 |
| `allowed_groups` | 群号白名单数组,如 `[123456, 789012]`;**留空 `[]` 表示所有群** |
| `create_issue` | 是否自动建 GitHub Issue |
| `github_repo` | 目标仓库,默认 `corazon1999/GrandUMI` |
| `agent_enabled` | 是否把新反馈送入本机 Agent 队列 |
| `agent_owner_qq` | 功能需求或不确定 Bug 需要确认时 @ 的管理员 QQ |
| `agent_notification_interval_seconds` | 管理员问题和玩家结果通知轮询秒数 |
| `chat_agent_enabled` | 是否轮询并发送聊天与 Bug 描述检查结果；普通聊天入口已关闭 |
| `chat_max_content_length` | 单条聊天正文最大字数，默认 500 |
| `admin_agent_enabled` | 是否启用管理员真实 @机器人后的独立全权限 Agent |
| `admin_agent_owner_qq` | 唯一管理员 QQ，固定为 `651846226` |
| `admin_agent_max_content_length` | 单条管理员任务正文最大字数，默认 3000 |
| `vision_enabled` | 是否允许读取聊天和 Bug 反馈中的图片，默认开启 |
| `vision_max_images` | 单条消息最多读取图片数，默认 4 |
| `vision_max_image_bytes` | 单张图片最大字节数，默认 8 MiB |
| `vision_media_ttl_seconds` | 未完成识别的服务器临时图片保留秒数，默认 86400 |
| `forward_max_nodes` | 合并转发最多展开的消息段数，默认 40 |
| `forward_max_depth` | 嵌套合并转发最大深度，默认 3 |
| `new_member_verification_enabled` | 是否启用新人邀请人验证；默认关闭 |
| `new_member_verification_groups` | **明确启用验证的群号数组**；空数组表示不对任何群生效，不会沿用“空白名单等于全部群”的规则 |
| `new_member_verification_timeout_seconds` | 历史字段名，现仅表示首次提示被 OneBot 确认送达后，等待一次追问的时长；默认 1800 秒（30 分钟），允许范围 60～86400 秒。它不再是回答期限，也不会触发自动移出 |
| `new_member_verification_poll_interval_seconds` | 重启恢复、API 重试与一次追问的后台轮询间隔，默认 300 秒（5 分钟），允许范围 1～3600 秒 |
| `group_add_auto_approval_enabled` | 是否启用加群申请自动审批；默认关闭。官方群继续核验邀请人，2 群对字段完整的申请直接通过 |
| `group_add_auto_approval_groups` | **明确启用自动审批的群号数组**；线上固定包含官方群 `297542853` 与 2 群 `524996856`，空数组表示不审批任何群，绝不表示全部群 |
| `abuse_moderation_enabled` | 是否启用群辱骂治理；只有 JSON 布尔值 `true` 才会启用 |
| `abuse_moderation_groups` | **明确启用治理的群号数组**；空数组永远表示不监听任何群，不继承 `allowed_groups` 的空白名单语义 |
| `abuse_moderation_exempt_qqs` | 追加豁免 QQ 数组；唯一管理员 `651846226` 与三个官方机器人无论是否填写都固定豁免 |
| `qq_whitelist_sync_enabled` | 是否启用游戏 QQ 白名单双群定时同步；默认关闭 |
| `qq_whitelist_sync_group_ids` | v2 权威数据源；一旦显式配置，必须按固定顺序完整填写 `[297542853, 524996856]`，增减、调序或重复都会失败关闭 |
| `qq_whitelist_sync_group_id` / `qq_whitelist_sync_group_name` | 仅保留给 v1 协议和旧私密配置兼容；旧配置中的群号必须仍为 `297542853`，部署迁移会另行补齐 v2 双群字段 |
| `qq_whitelist_sync_interval_hours` | 固定为 `2`，按 `Asia/Singapore` 墙钟的偶数整点执行 |
| `qq_whitelist_sync_timezone` | 固定为 `Asia/Singapore`（UTC+8） |
| `qq_whitelist_sync_endpoint` | 游戏服务受限内部 HTTPS 端点；跨主机禁止使用明文 HTTP |
| `qq_whitelist_sync_secret_env` | 读取随机密钥的环境变量名；配置文件中不得填写真实密钥 |
| `qq_whitelist_sync_min_members` | 两群合并、全局去重并排除已知官方机器人后的绝对人数下限，默认 100 |
| `qq_whitelist_sync_max_shrink_percent` | 合并结果相较上一成功快照允许的最大缩水比例，默认 25% |
| `qq_whitelist_sync_max_delay_seconds` | 每个偶数整点时隙允许的最长延迟，默认 600 秒；过期时隙不补发 |

### 群辱骂治理安全边界

本地样例默认关闭；服务器样例只为官方群 `297542853` 和 2 群 `524996856` 开启。实际上线由部署运维在私密配置中显式设置：

```json
{
  "abuse_moderation_enabled": true,
  "abuse_moderation_groups": [297542853, 524996856],
  "abuse_moderation_exempt_qqs": [651846226, 3215228879, 3430685803, 184689168]
}
```

- 权威执行者固定为 `id=primary, role=primary, expected_self_id=3215228879` 的 s-蛇；配置身份不符时机器人拒绝启动该功能。s-鹰、s-鲨即使收到同一群消息也不会查询成员或调用禁言动作。
- 只读取 `post_type=message`、`message_type=group` 的顶层结构化 `text` 与真实 `at` 段。不读取字符串 CQ 码、`raw_message`、引用、合并转发、图片或图片识别文字；缺少可信 OneBot `message_id` 时，为保证幂等只记录日志而不处罚。
- 判定词表保持短而保守：只匹配明确的第二人称严重人身攻击、家属诅咒、死亡威胁，或真实 @他人后的严重辱骂。普通负面评价、自嘲、技术讨论、敏感词测试、角色台词、引语、劝阻辱骂和仅仅出现某个词都不会处罚。外部文字不会进入 Agent、正则或命令执行。
- 命中后先调用不走缓存的 `get_group_member_info`。事件或实时结果显示为群主/群管理员、发送者是唯一管理员/官方机器人/显式豁免 QQ、成员响应字段矛盾、查询失败时，都不会调用禁言。
- 每个处罚在调用外部动作前写入 SQLite，以“群号 + 发送者 QQ + OneBot 消息号”永久去重。并发事件、三助理同时收取、容器重启和消息重放只能有一个调用者；审计仅保存规则编号与正文 SHA-256，不保存辱骂原文。
- OneBot 明确成功才记录 `confirmed`，明确拒绝记录 `rejected`。超时、断线、取消或进程在调用前后退出都属于可能已经生效的未知结果：预占记录保持一天重试屏障，既不宣称成功，也不会自动重试或延长处罚。同一成员在已有确认、未知或实时观察到的禁言窗口内出现后续排队消息时只记为抑制，不会反复把截止时间向后延长。
- 部署后应从日志确认目标群与唯一权威账号，再用普通测试成员发送一条明确攻击测试语句，核对 OneBot 只出现一次 `set_group_ban duration=86400`，并查询 SQLite `abuse_moderation_actions` 的 `confirmed` 状态。不得用群主、群管理员或豁免账号作为处罚验收对象。

### 加群申请自动审批安全边界

机器人 QQ 必须是目标群的群主或管理员。官方群 `297542853` 继续使用“需要回答问题并由管理员审核”，问题填写“请填写邀请人qq号”；2 群 `524996856` 的答案不参与审批。私密 `config.json` 仍须显式开启并填写两个固定目标群：

```json
{
  "group_add_auto_approval_enabled": true,
  "group_add_auto_approval_groups": [297542853, 524996856]
}
```

- 只处理目标群的 OneBot `post_type=request`、`request_type=group`、`sub_type=add` 事件；目标群列表为空或开关关闭时功能不会生效。`sub_type=invite` 表示邀请机器人自身入群，始终忽略，不会自动同意。
- 2 群字段完整的 `add` 请求直接使用事件原始 `flag` 调用 `set_group_add_request(sub_type=add, approve=true)`；无论 `comment` 是有效答案、乱填、空字符串还是缺失，都不会调用 `get_group_member_list`、解析或要求邀请人 QQ，也不会创建邀请人验证预备记录。
- 官方群仍先调用 `get_group_member_list(no_cache=true)`。NapCat 对普通主动申请和“群成员邀请好友且需管理员审核”均上报 `add`，但前者的 `user_id` 是申请人，后者是当前群内邀请人。事件 `user_id` 已在实时成员列表中时直接同意且不解析 `comment`；不在群时才按原规则解析唯一邀请人 QQ，并复用实时成员列表核验，答案无效、填写自己或机器人、邀请人不在群都会按原规则拒绝。
- 事件缺少有效 `flag`、`user_id` 或机器人身份时保持待审批。官方群成员查询失败，以及任一群审批动作被 OneBot 明确拒绝、超时、断线或取消时，都不会写入成功去重；NapCat 重投后可安全重试。同一进程内只有 OneBot 已确认成功的事件才按群和原始 `flag` 去重，并发重复事件由连接分发锁串行化后只审批一次。
- 这是独立于下方“入群后新人验证”的配置。官方群普通主动申请会在调用外部审批动作前持久保存“申请人、目标群、已审核邀请人”的不可改写预备记录；真实入群通知到达后，两项权威事实会在同一 SQLite 事务中直接合并为已登记。2 群直通分支不读取邀请人答案，也不创建这类记录；当前线上新人验证保持关闭。

### 新人验证安全边界

启用时必须同时填写开关和目标群，例如：

```json
{
  "new_member_verification_enabled": true,
  "new_member_verification_groups": [123456789],
  "new_member_verification_timeout_seconds": 1800,
  "new_member_verification_poll_interval_seconds": 300
}
```

- 只有目标群的 OneBot `group_increase` / `group_decrease` 通知，或同群已被 OneBot 明确同意的持久加群申请，才能建立登记资格。已有预审 QQ 的真实入群会直接形成终态；重复或并发通知不会重复登记、重置会话或重复确认。真实离群后再次入群会创建新一轮验证。若审批成功后的入群通知丢失，同一申请人发出的真实群消息仍可在授权有效期内恢复原有回答流程；未确认或已过期的审批记录不能由群消息恢复。
- 只有 `group_increase` 明确标记 `sub_type=invite`，且 `operator_id` 是有效、非新人、非机器人 QQ 时，才把操作者视为群成员直接邀请人。`approve` 的操作者通常是审批管理员，缺失、为零、矛盾或其他子类型的字段都不会被猜成邀请人，仍沿用新人回答流程。
- 回答者必须是待验证新人的事件 `user_id`，且消息必须包含顶层结构化 `at` 消息段并指向机器人自身。正文里的 `@` 字样、CQ 字符串、昵称、截图、引用和合并转发都不能冒充真实 @。
- 邀请人 QQ 只从该条消息的顶层文字或真实 @成员段提取，必须唯一，且不能填写新人自己或机器人。
- 目标验证群中，真实 @机器人并表达登记邀请人 QQ 的意图时，机器人会给出可直接照做的严格格式。只有刚入群且处于待验证状态的新人本人可以登记；其他成员不能代填，也不会因此获得会话或写入回答。审批结果仍待确认时会提示稍后重试，待验证者格式错误时会引导其只发送 `邀请人QQ：123456789`。意图识别只读取 OneBot 顶层文字和结构化真实 @；普通含 QQ 聊天、复制的文本 `@`、引用与转发内容都不会被误消费。
- 自动审批和可靠邀请通知会直接形成不可改写的邀请人登记；不会再提示新人重复声明，也不会写入一条伪造的群消息回答。只有没有可信邀请人记录的新人回答才会调用成员列表接口；接口失败时既不会通过，也不会丢失答案，SQLite 会保留答案并自动重试。一次追问不会查询成员列表，更不存在任何自动移出成员的调用。
- 首次提示动作没有得到 OneBot 成功响应时不会开始追问计时，后台会重试。极端情况下若 QQ 已收到首次提示、但进程在写入成功状态前崩溃，重启后可能重复提示一次；不会因此缩短玩家可回答的时间或误通过。
- 追问只有在 OneBot 确认发送成功后才会持久记录为已发送并清除计时，此后不再追问。发送失败会进行最多 5 次、有上限的退避重试；若持续失败或进程反复在发送租约内崩溃，系统会停止追问并保留无限期回答资格，避免无限刷屏。
- 旧版本遗留的 `checking_timeout` 或 `kicking` 会话会在数据库初始化时前向迁移为无限期 `pending`，同时清除旧截止时间、动作租约与踢人请求标记，绝不会恢复旧版自动移出流程。
- 5 分钟轮询只用于首次提示失败、成员 API 失败、重启恢复和一次追问等后台任务，因此这些动作最多可能再延后约 5 分钟。正常群消息回答仍由事件实时处理，新人入群时也会立即尝试发送第一次提示，不会等待下一轮后台轮询。

### 游戏 QQ 白名单双群每两小时同步

该功能默认关闭。启用后，`s-蛇` 每次都按 `Asia/Singapore` 墙钟重新计算
`00:00`、`02:00`、……、`22:00` 时隙，并通过 OneBot 的 `get_group_info` 和
`get_group_member_list` 依次实时读取固定群 `297542853` 与 `524996856`；两个动作都显式
使用 `no_cache=true`。每个群都必须通过群号、实时群名格式、接口成员数、成员唯一性与
前后身份稳定性检查。任一群读取或校验失败时，本时隙整体失败关闭，绝不会拿单群或上次
快照部分覆盖权威白名单。

两个完整快照先按 QQ 全局去重，再排除配置中三个官方机器人等已知助理账号；人数下限和
异常缩水门禁都针对最终并集。机器人会在调用游戏服务前持久保存两个来源群的实时元数据、
最终成员列表与 SHA-256。v2 操作键同时绑定固定双群集合、偶数整点时隙和最终并集摘要，
因此同一时隙的重复请求可幂等恢复，而乱序来源、不同快照、跨时隙重放不能冒用同一次操作。

游戏服务自身再次校验来源群集合、每群计数、最终唯一成员、摘要、时隙和人数门禁，并由
`QqAccessStore` 在共享账号 SQLite 的同一个立即事务内完成成员替换、版本递增、导入审计、
会话撤销和时隙幂等记录。测试服与正式服使用 `/data/grandumi-shared/accounts.db` 时只需
命中一次权威接口，不得分别更新两个环境。

只有权威事务确认提交后，机器人才能向两个来源群分别发送“白名单已更新”通知；每个群的
通知状态独立持久化，一个群明确发送失败不妨碍另一个群发送，失败群可有限重试。OneBot
超时、取消、断线或进程在发送期间退出都属于送达未知：该群会冻结为 `uncertain` 且不再
自动重发，另一群仍按自身状态继续，以避免未知结果下盲目产生重复群消息。两个群都确认
送达后才向游戏服务写入通知确认。

重启时只在当前偶数整点时隙的允许延迟窗口内恢复已经开始或已经提交的任务；不创建漏过的
旧时隙任务。为保持存量数据库和 HTTP 协议兼容，时隙仍分别使用 `scheduled_hour` 与
`scheduledHour` 字段，但 v2 强制其为偶数整点。机器人实例 ID、完整快照、提交状态和逐群
通知 outbox 均保存在 `feedback.db`；服务端以固定来源集合和时隙线性化并发请求。

测试与正式站点可能经过 CDN，不能依赖公网 DNS 回源后的 CDN 地址通过固定来源 ACL。
Compose 因此将 `test.grand-umi.com` 和唯一正式域名 `ygo.grand-umi.com` 固定解析到
`103.146.230.37`；请求仍使用原 HTTPS URL、TLS SNI 和 Host，并继续同时校验 Nginx
来源 ACL、固定代理标记和 Bearer 密钥。服务器 IP 迁移时必须把域名映射、来源 ACL 和
代理标记作为同一次受控变更更新，不能临时放宽为 Docker 网段或任意公网来源。

## 可切换人格 Agent

Bug 描述检查使用独立的只读队列和常驻工作器；普通群友的普通聊天不会进入该队列。女帝人格高傲、优雅且护短；娜美人格聪明干练、直率且刀子嘴豆腐心；罗宾人格冷静知性、温和并带有克制的幽默。玩家输入和图片只作为不可信数据，不会被当作工具指令。Bug 与管理员 Agent 图片先在服务器校验协议、公网地址、体积和文件头，再通过 SSH 拉到 `E:\GrandUMI-Temp\QQBotMedia`，校验 SHA-256 后使用 Codex `--image` 识别，任务结束立即清理。安装或更新本机工作器：

```powershell
cd D:\Self\GrandUMI-agent-runtime\repo\qq-bug-bot
.\install-chat-agent-worker.ps1

# 安装管理员专用全权限 Agent
.\install-admin-agent-worker.ps1
```

两个安装器自检通过后会分别注册并启动 `GrandUMI-Chat-Agent` 与 `GrandUMI-Admin-Agent` 登录任务。两者使用隐藏的 `pythonw.exe` 独立常驻、独立领取队列，避免长时间管理员任务阻塞普通聊天；日志分别位于 `chat-agent-worker.log` 与 `admin-agent-worker.log`。管理员任务原文通过标准输入传给 Codex，不放入进程命令行；工作器不向子进程传递环境变量中的令牌、密码或密钥，回群前还会阻断疑似凭据。`admin_agent_timeout_seconds` 默认为 7200 秒，租约至少比超时多 1800 秒，覆盖图片下载和桥接重试的最坏窗口；超时、取消或进程重启后会按已有队列状态有界重试，重试时先核对 Git、测试和部署实际状态，不盲目重复外部操作。

## Agent 自动分析与修复

启用后，服务端机器人只负责 QQ 会话、SQLite 队列和 GitHub Issue；本机
`agent_worker.py` 通过 SSH 领取任务，在独立 Git worktree 内调用 `codex exec`：

1. 只读分诊：只有明确违反既有规则/规格、置信度不低于 85 的 Bug 才自动修复。
2. 功能需求、规则歧义或信息不足时，在原反馈群 @ `651846226`；已回答但仍无法确定的任务直接转人工，不会反复询问。
3. 指定管理员 QQ `651846226` 直接发送 `#回复 具体判断` 即可，无需真正 @ 机器人。问题全局串行，因此无需填写反馈编号。
4. 修复 Agent 只能在 `workspace-write` 沙箱内修改代码和运行测试，不能提交或部署。
5. 独立复核 Agent 在一个可丢弃的 detached worktree 中重新检查 diff 并实际执行固定测试；新增的 `*.test.mjs` 会自动进入必跑测试。即使复核 Agent 违规修改文件，也只会丢弃隔离副本，不会污染原修复。
6. 复核不通过时，修复 Agent 默认在同一工作区内按复核意见修订 1 次；再次失败则转人工，不会清理后重新排队。
7. 固定程序核对路径、文件数、行数、测试事件、更新日志、远端快进状态后，才提交并运行 `deploy-test.ps1`。
8. 测试服外网验证成功后，机器人 @ 原玩家回复摘要、提交号和测试地址。

以下内容永不自动修改：仓库治理文件、CI、`ops/`、部署/发布脚本、依赖与项目清单、机器人自身、密钥和配置。命中门禁或有界修订后仍无法验证时会转人工，不会擅自放宽权限或循环重试。正式服发布不在本流程授权范围内。

### 安装本机工作器

前提：本机 `codex` 已登录且能访问模型，`git`、`ssh`、`py`、`powershell` 可用；
`D:\Self\GrandUMI-agent-runtime\repo` 是独立、干净的 `main` 副本。

```powershell
cd D:\Self\GrandUMI-agent-runtime\repo\qq-bug-bot
.\install-agent-worker.ps1
```

安装器会先运行队列、Git 同步和 Codex 自检；全部通过后才注册并启动当前用户的
`GrandUMI-Bug-Agent` 登录任务。工作器使用隐藏的 `pythonw.exe` 常驻运行，
Codex、SSH、Git 等子进程也会使用 Windows 无窗口模式，不会反复弹出黑色窗口。运行配置位于
`D:\Self\GrandUMI-agent-runtime\agent-worker.json`，日志位于其 `logs` 子目录。
`max_review_revisions` 控制独立复核后的有界修订次数（默认 1），
`max_transient_attempts` 控制模型或连接瞬时故障的最大尝试次数（默认 3）。

### 部署并启用服务器机器人

先部署代码但保持 Agent 关闭：

```powershell
.\deploy-bot-server.ps1
```

本机工作器自检通过后，再原子启用：

```powershell
.\deploy-bot-server.ps1 -EnableAgent
```

部署脚本不会复制或打印 `.env`、`config.server.json`、QQ 登录数据或反馈数据库；
它会构建并检查新容器，失败时恢复原文件与配置。部署时会在私密配置所在目录写入唯一临时文件，
同步数据和文件元数据后原子替换：仅当原群 `297542853` 已在某个现有作用域中时，才把 2 群
`524996856` 幂等追加到 `allowed_groups`、s-蛇的 `new_member_welcome_groups`、
`abuse_moderation_groups` 和 `group_add_auto_approval_groups`。迁移不会改变这些功能的
`enabled` 状态，也不会修改 s-鹰、s-鲨的欢迎配置或 `new_member_verification_*`。
对白名单同步，迁移会在确认旧群号仍为 `297542853` 后幂等写入固定的
`qq_whitelist_sync_group_ids=[297542853, 524996856]` 和 `qq_whitelist_sync_interval_hours=2`，
同时保留旧 `qq_whitelist_sync_group_id`、群名、密钥引用、未知字段和现有开关；已有但不符合
固定值的双群配置会中止部署，不会被静默覆盖。

## 三、运行

依赖只有 `websockets`,GitHub 走本机已登录的 `gh` CLI(无需 token)。

```powershell
cd D:\Self\GrandUMI\qq-bug-bot
py -m pip install -r requirements.txt
py bot.py
```

看到「已连接 NapCat,等待群消息…」即成功。可以在测试群用普通成员 @机器人验证固定回复，或发送一条含 `bug` 的消息验证描述检查。

> GitHub Issue 是通过本机 `gh issue create` 创建的,所以机器人必须跑在已 `gh auth login` 的这台机器上。

## 四、Linux 服务器 Docker 部署

推荐把三套 NapCat 和机器人放在同一个 Compose 项目中。三个账号分别使用独立容器、
主机名、MAC 和三组命名卷，严禁共享 QQ 登录目录。OneBot 端口只在 Docker 内网开放；
宿主机 WebUI 仅绑定 `127.0.0.1`：`s-蛇` 为 `6099`、`s-鹰` 为 `6100`、`s-鲨` 为
`6101`，避免管理端口暴露到公网。
Compose 当前锁定已经过服务器重建演练的 NapCat `v4.15.19` amd64 内容摘要；不要把
它改回 `latest`，也不要仅凭“密码登录成功”日志判断上线，必须同时验证 WebUI 在线和
OneBot `get_login_info`。

### 1. 创建服务器配置

```bash
cd /opt/qq-bug-bot
cp .env.example .env
cp config.server.example.json config.server.json
mkdir -p data
chmod 600 .env
chown root:10001 config.server.json
chmod 640 config.server.json
chown -R 10001:10001 data
```

编辑 `.env`：

- `GH_TOKEN` 使用只允许目标仓库创建 Issue 的细粒度 GitHub Token。
- `TZ` 使用服务器业务时区。
- `NAPCAT_ACCOUNT` 填 `s-蛇` QQ，`NAPCAT_EAGLE_ACCOUNT` 和
  `NAPCAT_SHARK_ACCOUNT` 分别填两个新账号 QQ；账号未知时保持为空，绝不能拿
  `s-蛇` 账号或卷代替。
- 三套 `HOSTNAME` 和 `MAC_ADDRESS` 在首次成功登录后必须各自固定，更改任一值都可能
  让 QQ 把容器识别为新设备。
- 白名单同步启用前，用 `openssl rand -hex 32` 生成
  `GRANDUMI_QQ_WHITELIST_SYNC_SECRET`；不得提交、打印或放进 `config.server.json`。

服务器还应保留权限为 `600` 的 `napcat-quick-password-md5.secret`。摘要只允许通过
Docker secret 和 PID 1 包装器进入支持该能力的 NapCat，不得写进 `.env`、Compose
环境或部署包。当前锁定的 `v4.15.19` 不读取该摘要；它仅用于后续受控升级的恢复，
升级镜像前必须重新完成登录、重建和 Docker daemon 恢复演练。

在 Windows 部署电脑上也可以运行以下脚本。脚本会隐藏输入、先验证目标仓库与
Issues 的读取权限,再通过 SSH 标准输入写入服务器,不会把 Token 放进命令行：

```powershell
.\configure-github-token.ps1
```

编辑 `config.server.json`：

- `ws_url` 保持 `ws://napcat:3001`。
- `access_token` 设置随机长字符串,并在 NapCat 的正向 WebSocket 配置中填写相同值。
- `assistant_connections` 中保留 `s-蛇` 的 `primary` 连接；`s-鹰`、`s-鲨` 分别连接
  `ws://napcat-eagle:3001`、`ws://napcat-shark:3001`。三个预期账号固定为
  `s-蛇 3215228879`、`s-鹰 3430685803`、`s-鲨 184689168`；两个副助理在扫码登录、
  配置 OneBot 并核验前保持 `enabled=false`。
- `allowed_groups` 填实际群号白名单；当前官方群范围为 `[297542853, 524996856]`，不要留空开放所有群。
- s-蛇必须已经加入 2 群，且要执行加群审批和辱骂禁言时必须具有群主或管理员权限；需要在 2 群使用
  已启用副助理的管理员任务入口时，也要先把对应副助理账号加入该群。
- 加群审批上线前必须只读核对私密配置仍为 `group_add_auto_approval_enabled=true`，且
  `group_add_auto_approval_groups` 同时包含 `297542853` 与 `524996856`；同时确认
  `new_member_verification_groups` 不包含 2 群。部署迁移只在原群已处于既有作用域时幂等追加 2 群，
  保留密钥、未知字段和其他开关，不会把无关群加入审批范围。
- 本次上线由部署运维在私密 `config.server.json` 中显式设置
  `abuse_moderation_enabled=true`、`abuse_moderation_groups=[297542853, 524996856]`，并确认
  s-蛇仍为 `expected_self_id=3215228879`；普通部署脚本保留现有开关，只镜像原群已有的作用域，
  不会把关闭或空列表解释成自动开启。
- 先保持 `qq_whitelist_sync_enabled=false`，优先部署兼容 v1 的游戏服务 v2 端点，并把服务端
  `GRANDUMI_QQ_WHITELIST_SYNC_GROUP_IDS` 明确设为 `297542853,524996856`。旧私密环境若只保留
  原群 `297542853` 会安全推导同一固定双群集合，但不应依赖该过渡行为长期运行。
- 游戏服务端点、Nginx 固定来源限制和两端同一份随机密钥全部就绪并通过 v1/v2 回归后，
  再在机器人私密配置中确认 `qq_whitelist_sync_group_ids=[297542853,524996856]`、
  `qq_whitelist_sync_interval_hours=2` 后开启。不得先单独上线 v2 机器人；服务端拒绝请求虽不会
  部分写入，但会让该时隙失败。
- 游戏服务器的 `/etc/grandumi/qq-whitelist-sync.env` 必须为 `root:root 0600`，并由目标
  后端在启动时实际加载。正式维护开始前只允许机器人指向已经启用的测试入口；正式
  后端尚未加载权限门时，不得提前改指向正式入口制造每两小时一次的失败请求。本节不构成
  候选服或正式服部署授权。

### 2. 启动和查看日志

从较新 NapCat 回退到 `v4.15.19` 时，应先在停止写入后备份三个命名卷。
`webui.json` 只保留旧版支持的 `host`、`prefix`、`port`、`token` 和 `loginRate`
字段，原 token 必须原样保留，旧文件应另存为权限 `600` 的时间戳备份；宿主机端口
仍只能发布到 `127.0.0.1:6099`。不要把 WebUI 或 OneBot 配置混入 QQ 登录卷。

```bash
docker compose config -q
docker compose build
docker compose up -d
docker compose logs -f --tail=200
```

NapCat 首次启动后,从部署电脑建立 SSH 隧道：

```powershell
ssh -L 6099:127.0.0.1:6099 root@服务器地址
```

然后分别在浏览器打开 `http://127.0.0.1:6099/webui`、`6100/webui`、`6101/webui`
完成对应 QQ 登录；每套 NapCat 都新增监听 `0.0.0.0:3001` 的正向 WebSocket 服务端，
并配置与该助理连接一致的访问令牌。两个新账号的逐项启用、恢复和回滚步骤见
[`三助理上线清单.md`](./三助理上线清单.md)。

每次登录态、镜像或设备身份调整后，至少执行两轮 `stop`、删除容器并重建；每轮都要
确认 NapCat 退出码为 `0`、WebUI 显示在线、OneBot `get_login_info` 返回正确账号。
最后重启一次 Docker daemon，确认 NapCat 自动恢复而机器人不会意外重复启动。
“快速登录开始”或密码接口返回成功都只是中间态，不能替代这些上线门禁。

### 3. 迁移和维护数据

正式切换前先停止本机机器人,再把旧库复制为服务器的
`/opt/qq-bug-bot/data/feedback.db`,避免两端同时消费同一条消息。

```bash
# 导出日报
docker compose exec bug-bot python export_by_date.py

# 查看反馈
docker compose exec bug-bot python -c \
  "import sqlite3; print(sqlite3.connect('/data/feedback.db').execute('select count(*) from feedback').fetchone())"

# 使用当前锁定摘要重建；升级摘要前必须先在隔离卷完成登录兼容性验证
docker compose up -d --build
```

必须备份 `data/` 目录和 NapCat 的三个命名卷。不要提交 `.env`、
`config.server.json`、`feedback.db`、密码摘要或任何登录信息。机器人停止后应确认
容器退出码为 `0`，并对 `feedback.db` 执行 SQLite `quick_check`；干净停止后不应残留
`feedback.db-wal` 或 `feedback.db-shm`。

## 五、查看反馈数据

所有反馈都存在同目录的 `feedback.db`(SQLite)。即使 GitHub 建 issue 失败,本地也一定有记录。

```powershell
py -c "import sqlite3; [print(r) for r in sqlite3.connect('feedback.db').execute('select id,qq,nickname,content,issue_no,created_at from feedback order by id desc')]"
```

## 文件结构

| 文件 | 作用 |
|------|------|
| `bot.py` | 主程序:连 OneBot WS、识别指令、调存储/建 issue、回执 |
| `abuse_moderation.py` | 只读取顶层结构化消息的保守辱骂判定器 |
| `storage.py` | SQLite 存储封装 |
| `github_issue.py` | 通过 gh CLI 建 GitHub Issue |
| `agent_bridge.py` | 服务器侧队列领取、问题和结果回写桥 |
| `agent_worker.py` | 本机 Codex 分诊、修复、复核、提交和测试服部署工作器 |
| `agent_protocol.py` | Agent 提示词和安全路径白名单 |
| `schemas/` | Codex 结构化分诊、修复和复核结果定义 |
| `config.json` | 你的实际配置(已 gitignore) |
| `feedback.db` | 运行时生成的反馈数据库(已 gitignore) |
