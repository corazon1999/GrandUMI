# EB04-007 / EB04-004 / EB04-003 — 只领袖的持续力量效果误加到全场

## 现象
- EB04-007 佐罗：【登场时】本应「我方**领袖**力量+2000」，实际变成**全场+2000**。
- 同类排查发现 EB04-004 卓夫、EB04-003 史莫格&塔西吉 有相同缺陷。

## 卡面原文
- EB04-007：【登场时】直到下个对方结束阶段结束，我方**领袖**力量+2000。
- EB04-004：【攻击时】直到下个对方结束阶段结束，我方**领袖**原本力量变为7000。
- EB04-003：【对方回合中】我方拥有《海军》特征的**领袖**原本力量变为7000。

## 根因（关键约定）
`ContinuousEffect.Scope`（Side / IncludeLeader / IncludeCharacters / Filter）**被引擎所有逻辑完全忽略**，仅用于快照显示（`PrivateStateSnapshotBuilder`）。
力量/费用/关键词/KO保护/无效化等所有消费点（`GameState.ContinuousPowerBonus` 等）**只调用 `eff.Predicate`**。

→ 约定：**Predicate 必须编码完整适用性（触发条件 + 作用于哪些卡）。Scope 只是显示元数据，写了不生效。**

这 3 张的作者把「只领袖」写进了 `Scope.Filter = c => c.Id == leaderId` / `IncludeCharacters = false`，但 Predicate 里漏了 `card.Id == leaderId`，于是对 owner 方所有卡都成立 → 全场加成（EB04-004 甚至把"7000-领袖基础力量"的增量加到全场）。

## 修复（定向，符合既有约定）
给 3 张的 Predicate 补 `&& card.Id == leaderId`：
- `EB04_007_Zoro.cs`、`EB04_004_Dorry.cs`：`sideIdx==owner && card.Id==leaderId && s.TurnCount<=baseTurn+1`
- `EB04_003_Smoker_Tashigi.cs`：`sideIdx==owner && card.Id==leaderId && card.Info.HasKeyword("海军") && s.CurrentTurnPlayer!=owner`

已审计全部 19 个 `IncludeCharacters=false`（只领袖）持续效果，其余 16 张 Predicate 均已正确限定领袖（如 `card.Id==selfId`/`==Leader.Id`），无需改。

## 为何不做系统级「让引擎消费 Scope」
更优雅，但有约 200 张持续效果卡设了 Scope 标志（大量 `IncludeLeader=false` 的角色增益），无法保证它们的 Scope 与 Predicate 一致，59 个测试覆盖不全，回归风险高。故按既有约定定向修，零风险。
**后续写「只领袖/只某类」持续效果时，务必把作用对象写进 Predicate，不能只靠 Scope。**

## 验证
- 后端 `dotnet build`：0 错误 0 警告。
- `dotnet test`：59 通过 0 失败。
