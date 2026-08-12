# 对战日志与 AI 训练数据设计

> 本文档是对战日志工作线的协作入口。目标是让 GrandUMI 的每一局对战都能被完整还原，并能在未来转化为训练卡牌机器人的有效数据输入。

## 1. 背景与目标

当前项目已经具备两条互补的数据链路：玩家端把自身收到的 `MsgGameState` 快照分块保存到浏览器 IndexedDB，供 `/replay/[id]` 本地回放；服务端则把公开快照、动作、随机事件和提示交互统一写入 `MatchLogs/{date}/{roomId}.jsonl`，供统计、审计、排障和后续训练导出使用。服务端不再重复生成 `Replays` 文件。

这套能力适合做基础视觉回放，但还不足以支撑完整复盘、确定性重放、bug 复现和 AI 训练。主要原因是当前记录的是脱敏后的观战快照，缺少双方隐藏信息、玩家原始动作、prompt 选择结果、随机性来源、合法动作集合和可训练的决策样本。

本工作线的最终目标：

- 对战日志可以完整还原整局对战，包括手牌、生命区、牌库顺序、场面、Don、临时效果、prompt 和战斗上下文。
- 对战日志可以用于确定性重放，便于定位 bug、验证规则实现、回归测试。
- 对战日志可以离线导出为 AI 训练样本，作为未来卡牌机器人训练的数据输入。
- 玩家体验不受影响。对局中只做低成本追加写入，复杂导出与分析放到对局结束后或离线工具中。

## 2. 现状

已有能力：

- `服务端WebSocket/Game/Logging/MatchLogRecorder.cs`：负责统一 jsonl 写盘。
- `服务端WebSocket/Game/GameRoomManager.cs`：创建房间时打开 match log 写入器。
- `服务端WebSocket/Game/GameEngine.cs`：每次 `Broadcast` 后写入一条 `kind = "public_snapshot"` 记录，并记录动作、随机事件和提示交互。
- `服务端WebSocket/Game/Snapshot/StateSnapshotBuilder.cs`：生成发给玩家或观战者的脱敏 `MsgGameState`。
- `opcgpro-web/src/data/matchRecorder.ts`：把玩家视角快照分块保存到浏览器 IndexedDB。
- `opcgpro-web/src/app/replay/[id]/page.tsx`：从浏览器 IndexedDB 读取本地回放。

当前不足：

- 回放日志使用观战视角快照，双方手牌、生命区、牌库顺序被脱敏。
- 没有独立记录客户端提交的原始动作。
- 没有记录动作校验结果，即 accepted / rejected。
- 没有记录 prompt 创建、响应、超时的完整链路。
- 洗牌使用普通 `Random`，没有对局 seed 或随机事件日志，无法稳定复现。
- 没有完整私有快照，不能保证日志可以从任意步骤恢复完整引擎状态。
- 没有训练样本导出格式，AI 无法直接消费当前 replay。

## 3. 设计原则

### 3.1 一份事件源，多种用途

不要在对局中同步写三份完全独立的文件。推荐先写一份统一的 `matchlog.v1.jsonl`，每行通过 `kind` 区分事件类型。后续可以从这份完整事件源导出：

- 玩家可看的脱敏回放。
- 服务端审计和 bug 复现日志。
- AI 训练样本。

这样既避免重复写盘，也保证三类数据来自同一事实来源。

### 3.2 对局中轻量，赛后重处理

对局进行中只做 append-only 写入，不能阻塞主结算流程。日志写入失败时应记录错误但不影响对战。

训练样本导出、压缩、统计、索引和质量检查都放到对局结束后或离线工具中。

### 3.3 公共回放脱敏，私有日志完整

玩家可访问的回放必须遵守隐藏信息规则，不暴露当时玩家不应知道的信息。

服务端内部日志和训练数据可以包含完整信息，但需要明确权限边界。未来如果支持分享回放，应从完整日志导出脱敏版本，而不是直接暴露完整日志。

### 3.4 优先保证可还原

训练数据可以后续迭代，但从第一天开始就必须记录足够的信息，使未来能够还原当时的对局。尤其是随机 seed、洗牌结果、原始动作、prompt 响应和完整状态。

## 4. 数据分层

这里的“三层”是用途分层，不要求一开始物理拆成三套实时写入系统。

### 4.1 Public Replay

用途：玩家观看回放、观战、分享。

特点：

- 使用脱敏视角。
- 不暴露双方当时不可见的手牌、生命区、牌库顺序。
- 可以由完整日志离线导出。
- 前端 `/replay/[id]` 消费这一层最合适。

### 4.2 Private Audit

用途：完整还原、bug 复现、规则回归测试、争议仲裁。

特点：

- 包含完整隐藏信息。
- 包含原始动作、校验结果、prompt 链路、随机事件。
- 可以从任意 checkpoint 恢复完整 `GameState`。
- 不面向普通玩家开放。

### 4.3 Training Dataset

用途：AI 卡牌机器人训练。

特点：

- 从完整 matchlog 离线导出。
- 以“决策点”为单位，不是简单逐 tick 复制 UI 快照。
- 重点字段包括 observation、legalActions、actionTaken、result、metadata。
- 可选包含 hiddenState，用于监督学习、调试或分析；真正模拟玩家视角训练时应剥离 hiddenState。

## 5. 统一 MatchLog 格式

建议文件位置：

```text
服务端WebSocket/MatchLogs/{yyyy-MM-dd}/{matchId}.jsonl
```

每一行都是一个独立 JSON 对象，采用统一 envelope：

```json
{
  "schema": "grandumi.matchlog.v1",
  "matchId": "ab12cd34ef56",
  "seq": 42,
  "tick": 17,
  "turn": 3,
  "phase": "Main",
  "timeUtc": "2026-05-27T15:30:00.000Z",
  "kind": "player_action_requested",
  "actor": 0,
  "payload": {}
}
```

字段说明：

| 字段 | 说明 |
|---|---|
| `schema` | 日志协议版本。用于未来兼容迁移。 |
| `matchId` | 对局 ID。建议继续使用 roomId 或在 roomId 外新增稳定 matchId。 |
| `seq` | 日志内单调递增序号。比 tick 更适合排序和校验。 |
| `tick` | 当前引擎 tick。保持和 `GameState.Tick` 对齐。 |
| `turn` | 当前回合数。 |
| `phase` | 当前阶段。 |
| `timeUtc` | 写入时间。 |
| `kind` | 事件类型。 |
| `actor` | 触发事件的玩家编号；系统事件可为空或 `-1`。 |
| `payload` | 事件载荷。 |

## 6. 事件类型

### 6.1 match_start

对局开始时写入。

建议 payload：

```json
{
  "players": [
    {
      "index": 0,
      "accountName": "Alice",
      "deckRaw": "OP15-001\nOP15-002\n...",
      "deckHash": "sha256..."
    },
    {
      "index": 1,
      "accountName": "Bob",
      "deckRaw": "OP15-001\nOP15-003\n...",
      "deckHash": "sha256..."
    }
  ],
  "firstPlayer": 0,
  "rulesVersion": "opcg-grandumi-v1",
  "cardDbVersion": "local-card-json-2026-05-27",
  "rngSeed": 123456789
}
```

说明：

- `deckRaw` 对训练和复现有用，但属于私有信息。
- 如果未来担心日志体积，可以保留 `deckHash`，完整 deck 进入 private section 或压缩归档。

### 6.2 private_snapshot

完整服务端状态快照。用于完整还原和 checkpoint。

建议在以下时机写入：

- 对局开始后。
- 每个 accepted action 结算后。
- 每个 prompt response 结算后。
- 对局结束时。

早期为了稳，可以每次状态变化都写完整快照。后续日志体积变大后，再优化为“事件 + 每 N 步 checkpoint”。

### 6.3 public_snapshot

脱敏快照，等价于当前给观战者看的 `MsgGameState`。

用途：

- 前端回放。
- 观战同步。
- 分享回放导出。

### 6.4 player_action_requested

客户端提交动作时立即记录。

建议 payload：

```json
{
  "action": "PlayCard",
  "data": {
    "handIndex": 2
  }
}
```

注意：这里记录的是原始输入，不代表动作合法。

### 6.5 player_action_accepted

动作通过校验并进入结算时记录。

建议 payload：

```json
{
  "action": "PlayCard",
  "normalized": {
    "handIndex": 2,
    "cardInstanceId": "guid",
    "cardNumber": "OP15-001"
  }
}
```

### 6.6 player_action_rejected

动作被拒绝时记录。

建议 payload：

```json
{
  "action": "PlayCard",
  "reason": "费用不足"
}
```

### 6.7 prompt_created

服务端创建 prompt 时记录。

建议 payload：

```json
{
  "promptId": "abc123",
  "playerIndex": 0,
  "kind": "SearchDeck",
  "validChoices": ["card-guid-1", "card-guid-2"],
  "minChoose": 0,
  "maxChoose": 1,
  "extra": {}
}
```

### 6.8 prompt_response

玩家响应 prompt 时记录。

建议 payload：

```json
{
  "promptId": "abc123",
  "chosen": ["card-guid-2"]
}
```

### 6.9 prompt_timeout

prompt 超时时记录。

建议 payload：

```json
{
  "promptId": "abc123"
}
```

### 6.10 random_event

任何会影响对局结果的随机事件都应该可复现。

推荐至少记录：

- 初始 seed。
- 每次 shuffle 的目标区域、玩家、shuffle 前后摘要。
- 早期也可以直接记录 shuffle 后完整顺序，优先保证可还原。

示例：

```json
{
  "type": "shuffle",
  "randomSeq": 1,
  "playerIndex": 0,
  "zone": "deck",
  "reason": "initial_setup",
  "rngSeed": 123456789,
  "count": 50,
  "beforeOrder": [{ "id": "card-guid-1", "number": "OP15-001" }],
  "afterOrder": [{ "id": "card-guid-3", "number": "OP15-003" }]
}
```

### 6.11 match_end

对局结束时记录。

建议 payload：

```json
{
  "winnerIndex": 1,
  "reason": "Surrender",
  "turnCount": 5,
  "finalTick": 38
}
```

## 7. 完整状态快照设计

当前 `StateSnapshotBuilder` 是面向客户端的脱敏视图，不适合作为完整还原数据。建议新增：

```text
服务端WebSocket/Game/Snapshot/PrivateStateSnapshotBuilder.cs
```

private snapshot 至少包含：

```json
{
  "roomId": "ab12cd34ef56",
  "tick": 17,
  "phase": "Main",
  "turnCount": 3,
  "currentTurnPlayer": 0,
  "firstPlayer": 0,
  "winnerIndex": null,
  "gameOverReason": null,
  "players": [
    {
      "index": 0,
      "accountName": "Alice",
      "leader": {},
      "hand": [],
      "characters": [],
      "stage": null,
      "trash": [],
      "deck": [],
      "life": [],
      "donDeckCount": 4,
      "costArea": [],
      "hasReDraw": false,
      "mulliganDone": true,
      "turnOnceUsed": []
    }
  ],
  "pendingPrompt": null,
  "currentBattle": null,
  "continuousEffects": []
}
```

每张卡建议统一使用 `CardInstanceSnapshot`：

```json
{
  "id": "guid",
  "number": "OP15-001",
  "name": "card name",
  "kind": "Character",
  "isTapped": false,
  "turnPlayed": 3,
  "powerModThisTurn": 0,
  "powerModThisBattle": 0,
  "powerModPersistent": 0,
  "costModThisTurn": 0,
  "costModPersistent": 0,
  "gainedKeywords": [],
  "restrictions": [],
  "cannotActivateNextReset": false,
  "isEffectsNullified": false
}
```

Don 建议记录：

```json
{
  "id": "optional-if-added",
  "state": "Active",
  "attachedToCardId": null
}
```

## 8. 训练样本格式

训练数据由离线工具从 `matchlog.v1.jsonl` 导出：

```text
tools/export-training-samples.mjs 或 服务端WebSocket/Tools/TrainingSampleExporter.cs
```

输出建议：

```text
服务端WebSocket/TrainingSamples/{yyyy-MM-dd}/{matchId}.training.v1.jsonl
```

每条样本对应一个决策点：

```json
{
  "schema": "grandumi.training_sample.v1",
  "matchId": "ab12cd34ef56",
  "decisionId": "ab12cd34ef56:42",
  "playerIndex": 0,
  "turn": 3,
  "phase": "Main",
  "observation": {},
  "legalActions": [],
  "actionTaken": {},
  "result": {
    "winnerIndex": 0,
    "isWin": true
  },
  "metadata": {
    "firstPlayer": 0,
    "leaderNumber": "OP15-001",
    "opponentLeaderNumber": "OP15-002",
    "rulesVersion": "opcg-grandumi-v1"
  },
  "hiddenState": {}
}
```

字段说明：

| 字段 | 说明 |
|---|---|
| `observation` | 当前玩家视角可见信息。训练玩家策略时主要使用它。 |
| `legalActions` | 当前合法动作集合。可以来自 `ActionValidator`。 |
| `actionTaken` | 实际玩家动作。 |
| `result` | 对局最终结果，可用于监督学习或强化学习回放。 |
| `metadata` | 规则版本、卡池版本、先后手、领航信息等。 |
| `hiddenState` | 可选完整状态。用于分析和调试，训练时可以剥离。 |

第一版训练样本不追求完美 reward shaping，只要能稳定表达“某状态下玩家选择了什么动作，以及最终胜负”即可。

## 9. 实施阶段

### 阶段 1：统一 matchlog 事件源

目标：从现在开始，每局对战都能产出结构稳定的 `matchlog.v1.jsonl`。

开发内容：

- 新增 `MatchLogRecorder`，保留 append-only jsonl 写入。
- 新增统一 envelope：`schema / matchId / seq / tick / turn / phase / timeUtc / kind / actor / payload`。
- 在房间创建时写入 `match_start`。
- 在动作入口记录 `player_action_requested`。
- 在动作校验失败时记录 `player_action_rejected`。
- 在状态广播时记录 `public_snapshot`。
- 在对局结束时记录 `match_end`。
- 保留或兼容当前 `ReplayRecorder`，避免一次性破坏现有回放路径。

验收标准：

- 完成一局对战后生成 `MatchLogs/{date}/{matchId}.jsonl`。
- 文件每行都是合法 JSON。
- 至少包含 `match_start`、玩家动作、public snapshot、match_end。
- 日志写入失败不影响对战。

### 阶段 2：完整还原与确定性重放

目标：日志足以完整还原整局对战。

开发内容：

- 新增 `PrivateStateSnapshotBuilder`。
- 写入 `private_snapshot`。
- 给每局生成并记录 `rngSeed`。
- 改造洗牌逻辑，保证 seed 可复现，并记录 `random_event`。
- 在 `PromptSystem` 中记录 `prompt_created / prompt_response / prompt_timeout`。
- 新增 `MatchLogReplayer` 或测试工具，读取日志并校验最终 private snapshot。

验收标准：

- private snapshot 包含双方完整手牌、生命区、牌库顺序和场面状态。
- 使用日志可以复现最终状态。
- prompt 选择链路完整可查。
- 洗牌和随机行为可复现。

### 阶段 3：AI 训练样本导出 MVP

目标：能从 matchlog 离线导出训练卡牌机器人可用的数据。

开发内容：

- 新增训练样本导出器。
- 在决策点生成 `observation / legalActions / actionTaken / result / metadata`。
- 第一版可选带 `hiddenState`，方便调试。
- 输出 `training.v1.jsonl`。
- 编写一个小型验证脚本，检查样本 JSON 合法性和关键字段完整性。

验收标准：

- 完成一局对战后可以离线导出训练样本。
- 每条样本都能对应到一个玩家决策点。
- 样本中有可见状态、合法动作、实际动作和最终胜负。
- 导出过程不影响玩家对战体验。

## 10. 开发落点

推荐新增或修改的文件：

```text
服务端WebSocket/Game/Logging/MatchLogRecorder.cs
服务端WebSocket/Game/Logging/MatchLogEntry.cs
服务端WebSocket/Game/Snapshot/PrivateStateSnapshotBuilder.cs
服务端WebSocket/Game/GameRoomManager.cs
服务端WebSocket/Game/GameEngine.cs
服务端WebSocket/Effects/PromptSystem.cs
服务端WebSocket/Game/Phase/TurnEngine.cs
服务端WebSocket/Effects/AtomicOps.cs
服务端WebSocket.Tests/MatchLogTests.cs
服务端WebSocket.Tests/Fixtures/matchlog-minimal.v1.jsonl
tools/export-training-samples.mjs
tools/verify-matchlog.mjs
```

前端第一阶段只需要尽量不破坏现有回放。等后端日志稳定后，再完善：

```text
opcgpro-web/src/app/replay/[id]/page.tsx
opcgpro-web/src/hooks/usePlayback.ts
opcgpro-web/src/types/playback.ts
```

## 11. 性能与体验

对局中日志写入可能带来额外 IO。为避免影响体验：

- 使用 append-only jsonl。
- 写入失败不能中断对局。
- 避免在主流程中做压缩、索引、训练样本生成。
- 第一版可以同步写入，后续如果发现 IO 压力再改为队列异步写入。
- private snapshot 会增加日志体积，早期优先正确性，后续再引入 checkpoint 间隔和压缩策略。

粗略判断：当前卡牌对局的动作频率远低于实时动作游戏，只要不在每帧写日志，按动作/prompt/tick 写 jsonl 通常不会明显影响玩家体验。

## 12. 安全与权限

完整日志包含隐藏信息，不应直接暴露给普通玩家。

建议规则：

- `MatchLogs` 作为服务端内部数据。
- 玩家回放从 `MatchLogs` 导出脱敏 `PublicReplay`。
- 训练样本如果包含 `hiddenState`，默认只用于本地或内部训练。
- 未来如果上传、分享或公开回放，只使用 public replay。

## 13. 回放与日志兼容策略

- 玩家当前设备上的回放继续使用浏览器 IndexedDB，不依赖服务器 `Replays` 目录。
- 服务端只写一份 `MatchLogs`，其中 `public_snapshot` 已包含生成公开回放所需的牌桌快照。
- 如果未来提供跨设备或分享回放，应从 `MatchLogs` 的 `public_snapshot` 导出脱敏 `PublicReplay`，不得重新引入整局双写。
- 完整日志可能包含隐藏信息，不能直接暴露给普通玩家。

## 14. 协作约定

开发这条线时请遵守：

- 修改日志 schema 时，同步更新本文档。
- 新增事件类型时，补充示例 payload。
- 任何影响还原能力的改动，都需要补充测试。
- 不要把完整 private log 直接接到玩家可访问 API。
- 训练样本导出逻辑优先做离线工具，不要放进对局实时链路。

## 15. 第一批任务清单

- [x] 新建 `Game/Logging` 模块。
- [x] 实现 `MatchLogRecorder`。
- [x] 创建对局时写入 `match_start`。
- [x] 动作入口写入 `player_action_requested`。
- [x] 动作拒绝时写入 `player_action_rejected`。
- [x] 广播时写入 `public_snapshot`。
- [x] 新增 `PrivateStateSnapshotBuilder`。
- [x] 写入 `private_snapshot`。
- [x] 记录 prompt 创建、响应、超时。
- [x] 记录 `rngSeed` 和 shuffle 事件。
- [x] 增加日志 JSON 合法性测试。
- [x] 增加 private snapshot 完整性测试。
- [x] 增加最小训练样本导出器。

当前最小验收命令：

```text
node tools/verify-matchlog.mjs 服务端WebSocket.Tests/Fixtures/matchlog-minimal.v1.jsonl
dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj
```

其中 `verify-matchlog.mjs` 也可以直接检查真实对局产出的 `MatchLogs/{yyyy-MM-dd}/{matchId}.jsonl`。

## 16. 阶段性结论

这次上线可以同时支持三阶段的基础能力，但实现重心应放在：

1. 日志协议一次设计到位。
2. 阶段 2 的完整还原能力尽早落地。
3. 阶段 3 先做离线导出 MVP。

不要一开始就建设复杂训练平台或回放管理后台。先确保每局对战都沉淀为高质量、可还原、可加工的原始数据，这样后续 AI 训练和规则验证才有可靠地基。
