---
卡号: 引擎/UI（波及 OP11-046、_GAP GERMA 光环，及所有 GrantRestriction=CannotBeRested 持续卡）
日期: 2026-06-20
现象: 「无法转为休息状态」状态在对战 UI 无任何可视提示；且其持续来源实际未被引擎拦截
根因: 见下
修复: 见下
波及卡牌: 见下
预防: 见下
---

# 引擎/UI — 「无法转为休息状态」状态图标 + RestCard 持续来源未拦截

## 现象
- OP16-030 等「下个重置不转活跃」加了 🔒 图标后，用户要求「无法转为休息状态」(`CannotBeRested`) 也加可视图标。
- 排查发现两件事：
  1. 该状态**根本没下发到对战 UI**（公开快照里没有这个字段）。
  2. 引擎对其**持续来源存在拦截缺陷**——`RestCard` 只拦瞬时来源，持续来源（如 OP11-046、GERMA 光环）的「无法被休息」形同虚设。

## 根因
1. **UI 链路缺字段**：公开快照 `StateSnapshotBuilder.fieldCards` 不含 `cannotBeRested`；`net.ts` / `gameStore.ts` / `FieldArea.tsx` 全链路均无此字段，客户端拿不到。
2. **引擎只拦一种来源**：`CannotBeRested` 有两种来源——
   - 瞬时：`AtomicOps.AddRestriction(c, CannotBeRested, 时长)` → 存 `card.Restrictions`，`c.HasRestriction(...)` 查（OP11-034 / OP14-033 / EB02-011 等）。
   - 持续：`ContinuousEffect.GrantRestriction = CannotBeRested` → `GameState.HasContinuousRestriction(c, ...)` 查，需要 `GameState` 上下文（OP11-046 自身、GERMA 光环等）。
   
   `AtomicOps.RestCard` 仅检查瞬时 `HasRestriction`，**持续来源未拦截**，这些卡的「无法被休息」实际不生效。

## 修复（后端中心化一处 + 前端全链路补字段）
**后端**
- `Effects/EffectRuntime.cs`：暴露 `public static GameState? CurrentState => _ambientAL.Value;`（AsyncLocal 当前对局，随效果上下文传播），供 `AtomicOps` 查持续限制。
- `Effects/AtomicOps.cs` `RestCard`：瞬时检查之后追加
  ```csharp
  var st = EffectRuntime.CurrentState;
  if (st is not null && st.HasContinuousRestriction(c, RestrictionKind.CannotBeRested)) return;
  ```
  `RestCard` 是「转休息」的唯一入口（上百个调用点全走它），中心化一处即修全。
- `Game/Snapshot/StateSnapshotBuilder.cs` `fieldCards`：新增 `cannotBeRested = 瞬时 HasRestriction || 持续 HasContinuousRestriction`。

**前端**
- `types/net.ts` `FieldCardSnapshot`、`store/gameStore.ts` `FieldCardView` 各加 `cannotBeRested: boolean`。
- `components/game/FieldArea.tsx`：角色卡**左下角**渲染「横置矩形 + 红×」内联 SVG（横置矩形=休息态卡牌，×=不能转入该状态），与右下角 🔒（下个重置不转活跃）区分。

## 波及卡牌
- **受 RestCard 拦截修复影响**（持续来源「无法被休息」现在真正生效）：OP11-046（自身条件性 GrantRestriction）、`_GAP` GERMA 光环，以及任何 `GrantRestriction=CannotBeRested` 的持续卡。
- **瞬时来源**（OP11-034 / OP14-033 / EB02-011 等 `AddRestriction`）原本已被拦截，行为不变，现在额外显示图标。
- 图标对全部 `CannotBeRested` 角色统一生效，无逐卡改动。

### 追加修复：OP16-032 / OP15-029 用错机制（占位关键字「禁止休息」）
- **现象**：OP16-032 波尔·汉库珂打出选目标后，既不显示图标，效果也没生效（对方仍能休息该角色）。
- **根因**：这两张「无法转为休息状态」用的是 `AtomicOps.GiveKeyword(target, "禁止休息", …)`，而 `"禁止休息"` 这个关键字**全仓库只写不读**——`RestCard` 不检查它、图标也不查它，是从未接入引擎的占位实现（OP15-029 原注释自承「作为示例注册」）。
- **修复**：两处统一改为标准的 `AtomicOps.AddRestriction(target, RestrictionKind.CannotBeRested, KeywordDuration.UntilNextOpponentEndPhase)`，一处改动同时修好效果（RestCard 拦截）与图标（快照 cannotBeRested）。
- **同款排查结论**：`"禁止休息"` 关键字仅 OP15-029、OP16-032 两处使用，已全部改正；其余「无法转休息」卡本就用 `AddRestriction`/`GrantRestriction`，无碍。
- **预防**：写「无法转休息」一律用 `RestrictionKind.CannotBeRested`（瞬时 `AddRestriction` 或持续 `GrantRestriction`），**禁止**再用自定义关键字这类未接入引擎的占位路径。

## 预防
- 新写「无法转休息」效果：瞬时（`AddRestriction`）或持续（`GrantRestriction=CannotBeRested`）均可，`RestCard` 已同时拦截两源，无需额外处理。
- **「状态要在 UI 体现」必须走全链路**：`StateSnapshotBuilder` → `net.ts FieldCardSnapshot` → `gameStore.ts FieldCardView` → 组件。注意 `FieldCardView` 是独立视图模型，只加 `net.ts` 会漏（本次首改即漏此层，tsc 报 TS2339 才发现）。

## 验证
- 后端 `dotnet build --nologo`：**0 错误 0 警告**。
- 前端 `npx tsc --noEmit`：**通过（EXIT=0）**。
