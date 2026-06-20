---
卡号: UI/全局（所有【启动主要】【每回合1次】卡）
日期: 2026-06-20
现象: 一回合一次的【启动主要】效果发动后，再点该卡仍浮现"启动效果"按钮
根因: 前端只看 effectTags 有无 ActivatedMain，没判断本回合是否已用；后端已用状态未下发
修复: 后端公开快照下发 activatedUsedThisTurn（领袖/角色/舞台），前端隐藏按钮
波及卡牌: 全部【启动主要】【每回合1次】卡统一生效
预防: 见下
---

# UI — 一回合一次的【启动主要】用后按钮未隐藏

## 现象
- 发动一回合一次的【启动主要】效果后，再点击该卡牌，右侧「启动效果」按钮仍然出现。
- 再点会让后端「空发动」（效果内部 oncePerTurn 去重跳过，不重复生效），但按钮该消失没消失。

## 根因
- 前端 `GameActions.tsx` 的 `canActivate` 只判断「选中卡的 effectTags 是否含 `ActivatedMain`」，**完全没判断本回合是否已发动过**。
- 后端「本回合已用」记录在 `PlayerState.TurnOnceUsed`，但**没下发到对战公开快照**（`StateSnapshotBuilder`），前端无从得知。
- `ActionValidator.CanUseEffect` 也不校验 oncePerTurn（只查回合/阶段/战斗/来源在场），所以重复点击是后端空发动。

## oncePerTurn key 两套（可判定）
- DSL 启动效果：`"{cardId}-Activated"`（`DslInterpreter.CheckOncePerTurn/MarkOncePerTurnUsed`）。
- 脚本启动效果：`"{番号}-act:{cardId}"`（脚本约定，`-act` 后缀；抽样 EB01-040/EB02-006/EB02-010/EB03-013 一致）。
- **只有 oncePerTurn 卡才写 `TurnOnceUsed`**，故「可多次发动」的启动效果天然不会被误判隐藏。

## 修复（后端权威下发 + 前端隐藏，5 文件）
**后端 `Game/Snapshot/StateSnapshotBuilder.cs`**：新增 helper
```csharp
static bool ActivatedUsedThisTurn(PlayerState p, CardInstance c)
    => p.TurnOnceUsed.Contains($"{c.Id}-Activated")
    || p.TurnOnceUsed.Contains($"{c.Info.Number}-act:{c.Id}");
```
- `fieldCards` 每张加 `activatedUsedThisTurn`；顶层加 `leaderActivatedUsedThisTurn` / `stageActivatedUsedThisTurn`（启动效果来源含领袖领航 / 角色 / 舞台三处）。

**前端**：
- `types/net.ts`：`FieldCardSnapshot` 加 `activatedUsedThisTurn`；`PlayerSnapshot` 加 `leaderActivatedUsedThisTurn` / `stageActivatedUsedThisTurn`。
- `store/gameStore.ts`：`FieldCardView` + `PlayerView` 同步加字段。
- `components/game/GameActions.tsx`：按选中的是领袖/舞台/角色取对应 `*ActivatedUsedThisTurn`，`canActivate` 末尾加 `&& !selectedActivatedUsed`。

## 波及卡牌
- 对全部【启动主要】【每回合1次】卡统一生效，无逐卡改动。
- 可多次发动的启动效果不受影响（恒 false）。

## 预防
- 「状态要在 UI 体现」走全链路：`StateSnapshotBuilder` → `net.ts` → `gameStore`(FieldCardView/PlayerView) → 组件。
- **新写启动脚本的 oncePerTurn key 务必遵循 `"{番号}-act:{id}"` 约定**，否则其「用后隐藏按钮」会失效（退化为按钮仍显示，不会误隐藏；但应统一）。
- 边角未处理：成本型（非 oncePerTurn）启动效果发动后成本不再满足时，按钮仍显示、点击空过——不在本次范围。

## 验证
- 后端 `dotnet build --nologo`：**0 错误 0 警告**。
- 前端 `npx tsc --noEmit`：**EXIT=0**。
